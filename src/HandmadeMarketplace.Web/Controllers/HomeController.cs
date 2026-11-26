using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using HandmadeMarketplace.Web.Models;
using HandmadeMarketplace.Application;

namespace HandmadeMarketplace.Web.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public async Task<IActionResult> Index([FromServices] IMarketplace marketplace,
        [FromServices] IWebHostEnvironment hosting, [FromQuery] Search search)
    {
        ViewBag.Categories = await marketplace.Categories();
        ViewBag.Search = search;
        // Installations can supply privately managed artwork without checking it into source.
        const string artwork = "/images/qazvin.jpg";
        var hasArtwork = hosting.WebRootFileProvider.GetFileInfo(artwork).Exists;
        if (hasArtwork) ViewData["HomeArtwork"] = artwork;
        var result = await marketplace.Search(search);
        var clean = Seo.CleanQuery(Request.Query, search.Page) && search.Page == result.Page && ModelState.IsValid;
        ViewData["Seo"] = new SeoPage(Seo.PageTitle(Seo.HomeTitle, result.Page), Seo.HomeDescription,
            clean ? Seo.PagePath("/", result.Page) : null, clean, hasArtwork ? Seo.Absolute(artwork) : null,
            Schema: clean && result.Page == 1 ? Seo.HomeSchema() : null);
        return View(result);
    }

    [HttpGet("/categories/{id:int}")]
    public async Task<IActionResult> Category(int id, int page, [FromServices] IMarketplace marketplace)
    {
        var category = (await marketplace.Categories()).FirstOrDefault(x => x.Id == id);
        if (category is null) return NotFound();
        var name = category.Name;
        return await Catalog(marketplace, new Search(CategoryId: id, Page: page == 0 ? 1 : page),
            name, $"آگهی‌های {name} در خودم‌ساز را ببینید و برای جزئیات هر ساخته با سازنده در ارتباط باشید.",
            Seo.CategoryPath(id));
    }

    [HttpGet("/qazvin")]
    public Task<IActionResult> Qazvin(int page, [FromServices] IMarketplace marketplace) =>
        Catalog(marketplace, new Search(City: Seo.Qazvin, Page: page == 0 ? 1 : page),
            "محصولات دست‌ساز و صنایع دستی قزوین",
            "محصولات دست‌ساز ثبت‌شده در شهر قزوین را در خودم‌ساز ببینید. برای شناخت هر ساخته، جزئیات آگهی و صفحه سازنده را بررسی کنید.",
            Seo.QazvinPath);

    private async Task<IActionResult> Catalog(IMarketplace marketplace, Search search, string heading,
        string description, string path)
    {
        var result = await marketplace.Search(search);
        if (search.Page != result.Page && Seo.CleanQuery(Request.Query, search.Page)) return NotFound();
        var index = result.Total > 0 && ModelState.IsValid && Seo.CleanQuery(Request.Query, search.Page);
        var canonical = Seo.PagePath(path, result.Page);
        ViewData["Seo"] = new SeoPage(Seo.PageTitle($"{heading} | {Seo.Brand}", result.Page), description,
            canonical, index, Schema: index ? Seo.Breadcrumbs((Seo.Brand, "/"), (heading, canonical)) : null);
        ViewData["CatalogPath"] = path;
        ViewData["Title"] = heading;
        ViewData["CatalogDescription"] = description;
        ViewBag.Search = search;
        return View("Catalog", result);
    }

    public IActionResult Privacy()
    {
        return View("Privacy.fa");
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
