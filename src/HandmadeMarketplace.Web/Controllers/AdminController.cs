using HandmadeMarketplace.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace HandmadeMarketplace.Web.Controllers;
[Authorize(Roles = "Admin")]
public class AdminController(IMarketplace market) : Controller
{
    public async Task<IActionResult> Index() => View(await market.Moderation());
    [HttpPost] public async Task<IActionResult> Moderate(int id, bool approve) { await market.Moderate(id, approve); TempData["Success"] = "وضعیت آگهی تغییر کرد"; return RedirectToAction(nameof(Index)); }
}
