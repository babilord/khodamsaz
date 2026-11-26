using System.Security.Claims;
using HandmadeMarketplace.Application;
using HandmadeMarketplace.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace HandmadeMarketplace.Web.Controllers;
[Authorize]
public class MessagesController(IMarketplace market) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    public async Task<IActionResult> Index() => View(await market.Inbox(UserId));
    public async Task<IActionResult> Thread(int id, string other) {
        var listing = await market.Get(id); if (listing == null || other == UserId) return NotFound();
        var messages = await market.Thread(id, UserId, other);
        if (messages.Count == 0 && (listing.MakerId != other || listing.Status != ListingStatus.Approved)) return NotFound();
        var maker = await market.Maker(other); if (maker == null) return NotFound();
        ViewBag.Listing = listing; ViewBag.Other = maker; return View(messages);
    }
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> UnreadCount() => Json(new { count = await market.UnreadCount(UserId) });
    [HttpPost]
    public async Task<IActionResult> Read(int id, string other, int throughId) {
        await market.MarkRead(id, UserId, other, throughId);
        return NoContent();
    }
    [HttpPost] public async Task<IActionResult> Send(int id, string other, string body) {
        try { await market.Send(id, UserId, other, body); } catch (ArgumentException e) { TempData["Error"] = e.Message; }
        return RedirectToAction(nameof(Thread), new { id, other });
    }
}
