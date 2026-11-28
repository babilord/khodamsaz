using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using HandmadeMarketplace.Application;
using HandmadeMarketplace.Infrastructure;
using HandmadeMarketplace.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HandmadeMarketplace.Web.Controllers;

[Route("account/access")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccessController(SmsOptions settings, MobileOtp otp, UserManager<AppUser> users,
    SignInManager<AppUser> signIn, IDataProtectionProvider protection, TimeProvider clock) : Controller
{
    private const string Binding = "unified-access";
    private const string CookieName = "khodamsaz.mobile-registration";
    private ITimeLimitedDataProtector State(string purpose) =>
        protection.CreateProtector("HandmadeMarketplace.Access.v1", purpose).ToTimeLimitedDataProtector();

    [HttpGet("")]
    public IActionResult Start(string? returnUrl) =>
        View("Start", new AccessPage(null, Local(returnUrl), SmsAvailable: settings.Enabled));

    [HttpPost("send")]
    public async Task<IActionResult> Send(string? phone, string? returnUrl, string? state, CancellationToken ct)
    {
        if (!settings.Enabled) return RedirectToAction("Login", "Account", new { returnUrl = Local(returnUrl) });
        var previous = Read("otp", state);
        phone = MobileNumber.Normalize(previous?.Phone ?? phone ?? "");
        returnUrl = Local(previous?.ReturnUrl ?? returnUrl);
        if (phone is null)
            return View("Start", new AccessPage(null, returnUrl, Error: "شماره موبایل معتبر وارد کنید."));
        var result = await otp.Issue(phone, MobileAccountController.ClientAddress(HttpContext),
            OtpPurpose.Access, Binding, ct);
        if (result.Status == OtpIssue.Wait)
            return previous is null
                ? View("Start", new AccessPage(phone, returnUrl, Error: "لطفاً کمی صبر کنید و دوباره تلاش کنید."))
                : Code(previous, "لطفاً کمی صبر کنید و دوباره تلاش کنید.");
        if (!result.Delivered)
            return View("Start", new AccessPage(phone, returnUrl, Error: "ارسال پیامک با مشکل مواجه شد. لطفاً دوباره تلاش کنید."));
        return Code(new Flow(phone, returnUrl, result.Id, clock.GetUtcNow()));
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify(string? state, string? code, CancellationToken ct)
    {
        if (!settings.Enabled) return RedirectToAction(nameof(Start));
        var flow = Read("otp", state);
        if (flow is null) return View("Start", new AccessPage(null, null, Error: "لطفاً کد جدید دریافت کنید."));
        if (flow.Issued + MobileOtp.Lifetime <= clock.GetUtcNow())
            return Code(flow, "اعتبار کد تأیید به پایان رسیده است. لطفاً کد جدید دریافت کنید.");
        if (!await otp.Verify(flow.Challenge, flow.Phone, OtpPurpose.Access, Binding, code ?? "", ct))
            return Code(flow, "کد تأیید واردشده صحیح نیست.");

        var user = await users.Users.SingleOrDefaultAsync(x => x.VerifiedMobile == flow.Phone, ct);
        if (user is not null)
        {
            if (!user.PhoneNumberConfirmed || !await MobileAccountController.Eligible(users, signIn, user))
                return View("Start", new AccessPage(flow.Phone, flow.ReturnUrl,
                    Error: "ورود با موبایل برای این حساب ممکن نیست. از روش ورود با ایمیل استفاده کنید."));
            var principal = await signIn.CreateUserPrincipalAsync(user);
            if (principal.Identity is not ClaimsIdentity identity || identity.HasClaim(x => x.Type == identity.RoleClaimType))
                return RedirectToAction("Login", "Account", new { returnUrl = flow.ReturnUrl });
            identity.AddClaim(new Claim(ClaimTypes.AuthenticationMethod, "sms"));
            await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal);
            return Continue(flow.ReturnUrl);
        }

        Response.Cookies.Append(CookieName, Protect("profile", flow, TimeSpan.FromMinutes(10)), ProfileCookie());
        return RedirectToAction(nameof(Complete));
    }

    [HttpGet("profile")]
    public IActionResult Complete()
    {
        var flow = ProfileFlow();
        return flow is null ? RedirectToAction(nameof(Start)) : Profile(flow, new());
    }

    [HttpPost("profile")]
    public async Task<IActionResult> Complete(NewMobileProfile input, CancellationToken ct)
    {
        var flow = ProfileFlow();
        if (flow is null) return RedirectToAction(nameof(Start));
        if (!string.IsNullOrEmpty(input.Password) && input.Password != input.Confirmation)
            ModelState.AddModelError(nameof(input.Confirmation), "تکرار رمز عبور با رمز عبور یکسان نیست.");
        if (!ModelState.IsValid) return Profile(flow, input);
        if (await users.Users.AnyAsync(x => x.VerifiedMobile == flow.Phone, ct))
            return Restart(flow);

        var email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim();
        var user = new AppUser
        {
            UserName = email ?? "mobile-" + Guid.NewGuid().ToString("N"), Email = email,
            DisplayName = input.Name.Trim(), City = input.City?.Trim() ?? "",
            PhoneNumber = flow.Phone, PhoneNumberConfirmed = true, VerifiedMobile = flow.Phone
        };
        IdentityResult result;
        try
        {
            result = string.IsNullOrEmpty(input.Password)
                ? await users.CreateAsync(user) : await users.CreateAsync(user, input.Password);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_AspNetUsers_VerifiedMobile" or "UserNameIndex" })
        {
            return Restart(flow);
        }
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.Errors.Any(x => x.Code.StartsWith("Duplicate", StringComparison.Ordinal))
                ? "این ایمیل یا شماره قابل استفاده نیست. اطلاعات را بررسی کنید."
                : "رمز عبور باید حداقل ۸ نویسه و شامل حروف بزرگ و کوچک، عدد و نشانه باشد.");
            return Profile(flow, input);
        }
        Response.Cookies.Delete(CookieName, ProfileCookie());
        await signIn.SignInAsync(user, isPersistent: false, authenticationMethod: "sms");
        return Continue(flow.ReturnUrl);
    }

    private IActionResult Restart(Flow flow)
    {
        Response.Cookies.Delete(CookieName, ProfileCookie());
        return RedirectToAction(nameof(Start), new { flow.ReturnUrl });
    }

    private string? Local(string? url) => Url.IsLocalUrl(url) ? url : null;
    private IActionResult Continue(string? url) => Local(url) is { } path
        ? LocalRedirect(path) : RedirectToAction("Index", "Home");

    private ViewResult Code(Flow flow, string? error = null)
    {
        var remaining = Math.Max(0, (int)Math.Ceiling((flow.Issued + MobileOtp.Cooldown - clock.GetUtcNow()).TotalSeconds));
        return View("Code", new AccessPage(flow.Phone, flow.ReturnUrl, Protect("otp", flow, TimeSpan.FromMinutes(15)), remaining, error));
    }

    private ViewResult Profile(Flow flow, NewMobileProfile input)
    {
        ViewData["VerifiedPhone"] = flow.Phone;
        return View("Complete", input);
    }

    private CookieOptions ProfileCookie() => new()
    {
        HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Strict,
        Path = "/account/access", MaxAge = TimeSpan.FromMinutes(10), IsEssential = true
    };

    private Flow? ProfileFlow() => settings.Enabled ? Read("profile", Request.Cookies[CookieName]) : null;

    private string Protect(string purpose, Flow flow, TimeSpan lifetime) =>
        State(purpose).Protect(JsonSerializer.Serialize(flow), lifetime);

    private Flow? Read(string purpose, string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 4096) return null;
        try { return JsonSerializer.Deserialize<Flow>(State(purpose).Unprotect(value)); }
        catch (CryptographicException) { return null; }
        catch (JsonException) { return null; }
    }

    private sealed record Flow(string Phone, string? ReturnUrl, Guid Challenge, DateTimeOffset Issued);
}
