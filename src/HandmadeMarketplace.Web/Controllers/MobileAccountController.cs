using System.Diagnostics;
using System.Security.Claims;
using HandmadeMarketplace.Application;
using HandmadeMarketplace.Infrastructure;
using HandmadeMarketplace.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HandmadeMarketplace.Web.Controllers;

[Route("account/mobile/{purpose}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MobileAccountController(SmsOptions settings, MobileOtp otp, UserManager<AppUser> users,
    SignInManager<AppUser> signIn) : Controller
{
    private const string InvalidCode = "کد وارد شده نامعتبر یا منقضی شده است.";
    private const string GenericIssue = "اگر امکان ارسال وجود داشته باشد، کد تأیید برای شماره واردشده ارسال می‌شود.";

    [HttpGet]
    public IActionResult Start([FromRoute] OtpPurpose purpose, string? returnUrl)
    {
        if (!Available(purpose)) return NotFound();
        if (purpose == OtpPurpose.VerifyPhone && User.Identity?.IsAuthenticated != true) return Challenge();
        return Form(purpose, new() { ReturnUrl = returnUrl });
    }

    [HttpPost("send")]
    public async Task<IActionResult> Send([FromRoute] OtpPurpose purpose, MobileOtpInput input, CancellationToken ct)
    {
        if (!Available(purpose)) return NotFound();
        if (purpose == OtpPurpose.VerifyPhone && User.Identity?.IsAuthenticated != true) return Challenge();
        var phone = MobileNumber.Normalize(input.Phone);
        if (phone is null) return Form(purpose, input, "شماره موبایل معتبر وارد کنید.");

        var started = Stopwatch.GetTimestamp();
        var subject = await ResolveSubject(purpose, phone);
        var binding = await GetBinding(purpose, phone, subject);
        var result = await otp.Issue(phone, ClientAddress(), purpose, binding, ct);
        input.Phone = phone;
        if (result.Status == OtpIssue.Wait)
            return Form(purpose, input, "لطفاً برای ارسال مجدد کد کمی صبر کنید.", 60);

        // Keep the response timing independent of whether an eligible account exists.
        if (!settings.Fake)
        {
            var remaining = TimeSpan.FromSeconds(settings.TimeoutSeconds + 0.25) - Stopwatch.GetElapsedTime(started);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, ct);
        }
        input.Challenge = result.Id;
        return Form(purpose, input, GenericIssue, 60);
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromRoute] OtpPurpose purpose, MobileOtpInput input, CancellationToken ct)
    {
        if (!Available(purpose)) return NotFound();
        if (purpose == OtpPurpose.VerifyPhone && User.Identity?.IsAuthenticated != true) return Challenge();
        var phone = MobileNumber.Normalize(input.Phone);
        if (phone is null) return Form(purpose, input, InvalidCode);
        if (!input.ValidDetails(purpose)) return Form(purpose, input, "اطلاعات فرم را کامل و درست وارد کنید.");

        var subject = await ResolveSubject(purpose, phone);
        var binding = await GetBinding(purpose, phone, subject);
        if (!await otp.Verify(input.Challenge, phone, purpose, binding, input.Code ?? "", ct))
            return Form(purpose, input, InvalidCode);

        try
        {
            if (purpose == OtpPurpose.Register)
            {
                var account = new AppUser
                {
                    UserName = input.Email!.Trim(), Email = input.Email.Trim(),
                    DisplayName = input.Name!.Trim(), City = input.City!.Trim(),
                    PhoneNumber = phone, PhoneNumberConfirmed = true, VerifiedMobile = phone
                };
                if (!(await users.CreateAsync(account, input.Password!)).Succeeded)
                    return Form(purpose, input, "ساخت حساب انجام نشد. اطلاعات ثبت‌نام را بررسی کنید.", reset: true);
                await signIn.SignInAsync(account, isPersistent: false, authenticationMethod: "sms");
            }
            else if (purpose == OtpPurpose.Login)
            {
                if (!await Eligible(subject)) return Form(purpose, input, InvalidCode, reset: true);
                var principal = await signIn.CreateUserPrincipalAsync(subject!);
                if (principal.Identity is not ClaimsIdentity identity || identity.HasClaim(x => x.Type == identity.RoleClaimType))
                    return Form(purpose, input, InvalidCode, reset: true);
                identity.AddClaim(new Claim(ClaimTypes.AuthenticationMethod, "sms"));
                await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal,
                    new AuthenticationProperties { IsPersistent = false });
            }
            else if (purpose == OtpPurpose.RecoverPassword)
            {
                if (!await Eligible(subject)) return Form(purpose, input, InvalidCode, reset: true);
                var token = await users.GeneratePasswordResetTokenAsync(subject!);
                if (!(await users.ResetPasswordAsync(subject!, token, input.Password!)).Succeeded)
                    return Form(purpose, input, "بازیابی انجام نشد. رمز عبور تازه را بررسی کنید.", reset: true);
                return RedirectToAction("Login", "Account");
            }
            else
            {
                if (!await Eligible(subject) ||
                    !(await signIn.CheckPasswordSignInAsync(subject!, input.Password!, lockoutOnFailure: true)).Succeeded)
                    return Form(purpose, input, "تأیید شماره انجام نشد. رمز عبور را بررسی کنید.", reset: true);
                subject!.PhoneNumber = phone;
                subject.PhoneNumberConfirmed = true;
                subject.VerifiedMobile = phone;
                if (!(await users.UpdateSecurityStampAsync(subject)).Succeeded)
                    return Form(purpose, input, "تأیید شماره انجام نشد.", reset: true);
                await signIn.RefreshSignInAsync(subject);
                return RedirectToAction("Profile", "Account");
            }
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_AspNetUsers_VerifiedMobile" or "UserNameIndex" })
        {
            return Form(purpose, input, "انجام درخواست ممکن نبود. لطفاً دوباره تلاش کنید.", reset: true);
        }
        return Url.IsLocalUrl(input.ReturnUrl) ? LocalRedirect(input.ReturnUrl) : RedirectToAction("Index", "Home");
    }

    private bool Available(OtpPurpose purpose) => settings.Enabled && ModelState.IsValid && Enum.IsDefined(purpose);

    private async Task<AppUser?> ResolveSubject(OtpPurpose purpose, string phone) => purpose switch
    {
        OtpPurpose.VerifyPhone => await users.GetUserAsync(User),
        OtpPurpose.Register => null,
        _ => await users.Users.SingleOrDefaultAsync(x => x.VerifiedMobile == phone && x.PhoneNumberConfirmed)
    };

    private async Task<string?> GetBinding(OtpPurpose purpose, string phone, AppUser? subject)
    {
        if (purpose is OtpPurpose.Register or OtpPurpose.VerifyPhone &&
            await users.Users.AnyAsync(x => x.VerifiedMobile == phone && (subject == null || x.Id != subject.Id)))
            return null;
        if (purpose == OtpPurpose.Register) return "new-account";
        return await Eligible(subject) ? subject!.Id + ":" + await users.GetSecurityStampAsync(subject) : null;
    }

    // Passwordless login and reset cannot replace MFA or the existing elevated-account flow.
    private async Task<bool> Eligible(AppUser? subject) => subject is not null
        && await signIn.CanSignInAsync(subject)
        && !await users.IsLockedOutAsync(subject)
        && !await users.GetTwoFactorEnabledAsync(subject)
        && (await users.GetRolesAsync(subject)).Count == 0;

    private string ClientAddress()
    {
        var address = HttpContext.Connection.RemoteIpAddress;
        if (address?.IsIPv4MappedToIPv6 == true) address = address.MapToIPv4();
        if (address?.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var prefix = address.GetAddressBytes();
            Array.Clear(prefix, 8, 8);
            return new System.Net.IPAddress(prefix) + "/64";
        }
        return address?.ToString() ?? "unavailable";
    }

    private ViewResult Form(OtpPurpose purpose, MobileOtpInput input, string? message = null,
        int cooldown = 0, bool reset = false)
    {
        if (reset) input.Challenge = Guid.Empty;
        input.Code = input.Password = null;
        return View("Mobile", new MobileOtpPage(purpose, input, message, cooldown));
    }
}
