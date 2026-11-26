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
        var u = (await users.GetUserAsync(User))!; u.DisplayName = input.Name.Trim(); u.City = input.City.Trim(); u.Bio = input.Bio ?? "";
        var result = await users.UpdateAsync(u); if (!result.Succeeded) { ModelState.AddModelError("", "ذخیره انجام نشد"); return View(input); }
        TempData["Success"] = "پروفایل ذخیره شد"; return RedirectToAction(nameof(Profile));
    }
    public IActionResult Denied() { Response.StatusCode = 403; return View(); }
}
