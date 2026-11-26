using System.Security.Claims;
using HandmadeMarketplace.Application;
using HandmadeMarketplace.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HandmadeMarketplace.Web.Controllers;

[Authorize]
public sealed class PurchaseDeclarationsController(IPurchaseDeclarations declarations) : Controller
{
    public const string LimitMessage = "شما حداکثر ۱۰ بار خرید این محصول را ثبت کرده‌اید.";

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromForm] int productId, [FromForm] Guid requestId,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();
        if (!ModelState.IsValid || productId <= 0 || requestId == Guid.Empty)
            return BadRequest(new { message = "درخواست ثبت خرید معتبر نیست." });

        var result = await declarations.Declare(productId, userId, requestId, cancellationToken);
        if (result.Outcome == PurchaseOutcome.NotFound)
            return NotFound(new { message = "این محصول برای ثبت خرید در دسترس نیست." });
        if (result.Outcome == PurchaseOutcome.OwnProduct)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "نمی‌توانید خرید محصول خودتان را ثبت کنید." });

        var stats = result.Statistics!;
        var remaining = PurchaseDeclaration.Limit - stats.PersonalCount;
        var response = new
        {
            uniqueBuyers = stats.UniqueBuyers,
            total = stats.Total,
            remaining,
            message = result.Outcome == PurchaseOutcome.LimitReached
                ? LimitMessage : "خرید شما با موفقیت ثبت شد."
        };
        return result.Outcome == PurchaseOutcome.LimitReached ? Conflict(response) : Ok(response);
    }
}
