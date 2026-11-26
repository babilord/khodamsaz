using System.Security.Claims;
using HandmadeMarketplace.Application;
using HandmadeMarketplace.Domain;
using HandmadeMarketplace.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace HandmadeMarketplace.Web.Controllers;
public class ListingsController(IMarketplace market, IWebHostEnvironment environment) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    public async Task<IActionResult> Details(int id) {
        var listing = await market.Get(id);
        if (listing == null || (listing.Status != ListingStatus.Approved && listing.MakerId != UserId && !User.IsInRole("Admin"))) return NotFound();
        ViewData["Seo"] = Seo.Product(listing, Seo.CleanQuery(Request.Query));
        ViewBag.Maker = await market.Maker(listing.MakerId); return View(listing);
    }
    public async Task<IActionResult> Maker(string id) {
        var maker = await market.Maker(id); if (maker == null) return NotFound();
        var listings = await market.ByMaker(id);
        var path = Seo.MakerPath(id);
        var index = listings.Count > 0 && Seo.CleanQuery(Request.Query);
        var description = Seo.Summary(maker.Bio,
            string.IsNullOrWhiteSpace(maker.City)
                ? $"با {maker.Name} و آگهی‌های ثبت‌شده این سازنده در خودم‌ساز آشنا شوید."
                : $"ساخته‌های {maker.Name} در {maker.City} را در خودم‌ساز ببینید.");
        ViewData["Seo"] = new SeoPage($"{maker.Name} | سازندگان {Seo.Brand}", description, path, index,
            Schema: index ? Seo.Breadcrumbs((Seo.Brand, "/"), (maker.Name, path)) : null);
        ViewBag.Maker = maker; return View(listings);
    }
    [Authorize] public async Task<IActionResult> Mine() => View(await market.Mine(UserId));
    [Authorize] public async Task<IActionResult> Edit(int id = 0) {
        var input = new ListingInput();
        if (id != 0) {
            var l = await market.Get(id); if (l == null || l.MakerId != UserId) return NotFound();
            input = new() { Id = l.Id, Title = l.Title, Description = l.Description, City = l.City, Price = l.Price, CategoryId = l.CategoryId }; ViewBag.Images = l.Images;
        }
        ViewBag.Categories = await market.Categories(); return View(input);
    }
    [Authorize, HttpPost, RequestSizeLimit(32 * 1024 * 1024)]
    public async Task<IActionResult> Edit(ListingInput input, List<IFormFile> images, int[] removeImages) {
        var existing = input.Id == 0 ? null : await market.Get(input.Id);
        var removedPaths = existing?.Images.Where(x => removeImages.Contains(x.Id)).Select(x => x.Path).ToArray() ?? [];
        if (input.Id != 0 && (existing == null || existing.MakerId != UserId)) return NotFound();
        if (images.Count + (existing?.Images.Count(x => !removeImages.Contains(x.Id)) ?? 0) > 6) ModelState.AddModelError("", "حداکثر شش تصویر مجاز است.");
        var validated = new List<(byte[] Bytes, string Extension)>();
        foreach (var image in images) {
            if (image.Length is <= 0 or > 5 * 1024 * 1024) { ModelState.AddModelError("", "اندازه هر تصویر باید حداکثر ۵ مگابایت باشد."); continue; }
            using var buffer = new MemoryStream(); await image.CopyToAsync(buffer); var bytes = buffer.ToArray();
            var ext = bytes.Length >= 8 && bytes.Take(8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) ? ".png" : bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 ? ".jpg" : null;
            if (ext == null) ModelState.AddModelError("", "فقط تصاویر JPEG و PNG مجازند."); else validated.Add((bytes, ext));
        }
        var saved = new List<string>();
        var committed = false;
        if (ModelState.IsValid) {
            try {
                var folder = Path.Combine(environment.WebRootPath, "uploads"); Directory.CreateDirectory(folder);
                foreach (var file in validated) { var name = Guid.NewGuid().ToString("N") + file.Extension; await System.IO.File.WriteAllBytesAsync(Path.Combine(folder, name), file.Bytes); saved.Add("/uploads/" + name); }
                await market.Save(input, UserId, saved, removeImages);
                committed = true;
                foreach (var path in removedPaths) DeleteUpload(path);
                TempData["Success"] = "آگهی ذخیره شد و پس از تأیید مدیر نمایش داده می‌شود."; return RedirectToAction(nameof(Mine));
            } catch (ArgumentException e) { ModelState.AddModelError("", e.Message); }
            finally { if (!committed) foreach (var path in saved) DeleteUpload(path); }
        }
        ViewBag.Categories = await market.Categories(); ViewBag.Images = existing?.Images; return View(input);
    }
    private void DeleteUpload(string path) {
        if (!path.StartsWith("/uploads/", StringComparison.Ordinal) || Path.GetFileName(path) != path[9..]) return;
        try { System.IO.File.Delete(Path.Combine(environment.WebRootPath, "uploads", Path.GetFileName(path))); }
        catch (IOException) { /* A locked orphan may be cleaned during maintenance. */ }
        catch (UnauthorizedAccessException) { }
    }
    [Authorize, HttpPost] public async Task<IActionResult> Delete(int id) {
        var listing = await market.Get(id); if (listing == null || listing.MakerId != UserId) return NotFound();
        var paths = listing.Images.Select(x => x.Path).ToArray();
        if (!await market.Delete(id, UserId)) return NotFound();
        foreach (var path in paths) DeleteUpload(path);
        TempData["Success"] = "آگهی حذف شد"; return RedirectToAction(nameof(Mine));
    }
    [Authorize, HttpPost] public async Task<IActionResult> Sold(int id) { if (!await market.MarkSold(id, UserId)) return NotFound(); return RedirectToAction(nameof(Mine)); }
    [Authorize] public async Task<IActionResult> Favorites() => View(await market.Favorites(UserId));
    [Authorize, HttpPost] public async Task<IActionResult> Favorite(int id) { await market.ToggleFavorite(id, UserId); TempData["Success"] = "فهرست علاقه‌مندی‌ها به‌روز شد"; return RedirectToAction(nameof(Favorites)); }
}
