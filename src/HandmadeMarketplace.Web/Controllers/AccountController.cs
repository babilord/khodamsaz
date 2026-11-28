using HandmadeMarketplace.Infrastructure;
using HandmadeMarketplace.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
namespace HandmadeMarketplace.Web.Controllers;
public class AccountController(UserManager<AppUser> users, SignInManager<AppUser> signIn) : Controller
{
    public IActionResult Login() => View(new LoginInput());
    [HttpPost] public async Task<IActionResult> Login(LoginInput input, string? returnUrl) {
        if (ModelState.IsValid) {
            var result = await signIn.PasswordSignInAsync(input.Email, input.Password, false, true);
            if (result.Succeeded) return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction("Index", "Home");
            ModelState.AddModelError("", result.IsLockedOut ? "ورود موقتاً قفل شده است." : "ایمیل یا رمز عبور نادرست است.");
        } return View(input);
    }
    public IActionResult Register() => View(new RegisterInput());
    [HttpPost] public async Task<IActionResult> Register(RegisterInput input) {
        if (ModelState.IsValid) {
            var user = new AppUser { UserName = input.Email, Email = input.Email, DisplayName = input.Name.Trim(), City = input.City.Trim() };
            var result = await users.CreateAsync(user, input.Password);
            if (result.Succeeded) { await signIn.SignInAsync(user, false); return RedirectToAction("Index", "Home"); }
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Code.Contains("Duplicate") ? "این ایمیل قبلاً ثبت شده است." : "رمز باید حداقل ۸ نویسه و شامل حروف بزرگ و کوچک، عدد و نشانه باشد.");
        } return View(input);
    }
    [Authorize, HttpPost] public async Task<IActionResult> Logout() { await signIn.SignOutAsync(); return RedirectToAction("Index", "Home"); }
    [Authorize] public async Task<IActionResult> Profile() { var u = (await users.GetUserAsync(User))!; return View(new ProfileInput { Name = u.DisplayName, City = u.City, Bio = u.Bio }); }
    [Authorize, HttpPost] public async Task<IActionResult> Profile(ProfileInput input) {
        if (!ModelState.IsValid) return View(input);
        var u = (await users.GetUserAsync(User))!; u.DisplayName = input.Name.Trim(); u.City = input.City?.Trim() ?? ""; u.Bio = input.Bio ?? "";
        var result = await users.UpdateAsync(u); if (!result.Succeeded) { ModelState.AddModelError("", "ذخیره انجام نشد"); return View(input); }
        TempData["Success"] = "پروفایل ذخیره شد"; return RedirectToAction(nameof(Profile));
    }
    [Authorize, HttpGet]
    public async Task<IActionResult> Credentials()
    {
        var user = await CredentialAccount();
        return user is null ? Forbid() : await CredentialsView(user);
    }

    [Authorize, HttpPost]
    public async Task<IActionResult> SaveEmail(string? email, string? currentPassword)
    {
        var user = await CredentialAccount();
        if (user is null) return Forbid();
        email = email?.Trim();
        if (email is null || email.Length > 256 || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            return await CredentialsView(user, "ایمیل معتبر وارد کنید.");
        if (await users.HasPasswordAsync(user) &&
            !(await signIn.CheckPasswordSignInAsync(user, currentPassword ?? "", lockoutOnFailure: true)).Succeeded)
            return await CredentialsView(user, "رمز عبور فعلی تأیید نشد.");
        if (string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase))
            return RedirectToAction(nameof(Credentials));

        user.UserName = email;
        user.Email = email;
        user.EmailConfirmed = false;
        try
        {
            if (!(await users.UpdateSecurityStampAsync(user)).Succeeded)
                return await CredentialsView(user, "این ایمیل قابل استفاده نیست.");
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException error) when (error.InnerException is Npgsql.PostgresException
            { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation, ConstraintName: "UserNameIndex" })
        {
            return await CredentialsView(user, "این ایمیل قابل استفاده نیست.");
        }
        await signIn.RefreshSignInAsync(user);
        TempData["Success"] = "ایمیل ذخیره شد.";
        return RedirectToAction(nameof(Credentials));
    }

    [Authorize, HttpPost]
    public async Task<IActionResult> SavePassword(string? password, string? confirmation, string? currentPassword)
    {
        var user = await CredentialAccount();
        if (user is null) return Forbid();
        if (string.IsNullOrEmpty(password) || password.Length > 128 || password != confirmation)
            return await CredentialsView(user, "رمز عبور و تکرار آن را یکسان وارد کنید.");
        var hasPassword = await users.HasPasswordAsync(user);
        if (hasPassword &&
            !(await signIn.CheckPasswordSignInAsync(user, currentPassword ?? "", lockoutOnFailure: true)).Succeeded)
            return await CredentialsView(user, "رمز عبور فعلی تأیید نشد.");
        var result = hasPassword
            ? await users.ChangePasswordAsync(user, currentPassword!, password)
            : await users.AddPasswordAsync(user, password);
        if (!result.Succeeded)
            return await CredentialsView(user, "رمز عبور باید حداقل ۸ نویسه و شامل حروف بزرگ و کوچک، عدد و نشانه باشد.");
        await signIn.RefreshSignInAsync(user);
        TempData["Success"] = "رمز عبور ذخیره شد.";
        return RedirectToAction(nameof(Credentials));
    }

    private async Task<AppUser?> CredentialAccount()
    {
        var user = await users.GetUserAsync(User);
        return user is { PhoneNumberConfirmed: true, VerifiedMobile: not null } &&
            await MobileAccountController.Eligible(users, signIn, user) ? user : null;
    }

    private async Task<ViewResult> CredentialsView(AppUser user, string? error = null)
    {
        if (error is not null) ModelState.AddModelError("", error);
        ViewData["Email"] = user.Email;
        ViewData["HasPassword"] = await users.HasPasswordAsync(user);
        return View("Credentials");
    }


    public IActionResult Denied() { Response.StatusCode = 403; return View(); }
}
