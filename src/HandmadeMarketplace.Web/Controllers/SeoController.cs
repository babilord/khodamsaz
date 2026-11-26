using System.Xml.Linq;
using HandmadeMarketplace.Domain;
using HandmadeMarketplace.Infrastructure;
using HandmadeMarketplace.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HandmadeMarketplace.Web.Controllers;

public sealed class SeoController(MarketplaceDb database) : Controller
{
    private const int SitemapSize = 45_000;
    private static readonly XNamespace SitemapNamespace = "http://www.sitemaps.org/schemas/sitemap/0.9";

    [HttpGet("/robots.txt")]
    public ContentResult Robots() => Content($"User-agent: *\nAllow: /\nSitemap: {Seo.Absolute("/sitemap.xml")}\n", "text/plain; charset=utf-8");

    [HttpGet("/sitemap.xml")]
    public Task<IActionResult> Sitemap(CancellationToken cancellationToken) => SitemapDocument(null, cancellationToken);

    [HttpGet("/sitemap-{part:int}.xml")]
    public Task<IActionResult> SitemapPart(int part, CancellationToken cancellationToken) => SitemapDocument(part, cancellationToken);

    private async Task<IActionResult> SitemapDocument(int? part, CancellationToken cancellationToken)
    {
        var publicListings = database.Listings.AsNoTracking().Where(x => x.Status == ListingStatus.Approved);
        var listings = await publicListings.OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.CategoryId, x.Category.ParentId, x.MakerId, x.City })
            .ToListAsync(cancellationToken);

        var paths = new List<string> { "/" };
        if (listings.Any(x => x.City == Seo.Qazvin)) paths.Add(Seo.QazvinPath);
        paths.AddRange(listings.SelectMany(x => new[] { x.CategoryId, x.ParentId ?? x.CategoryId })
            .Distinct().Order().Select(Seo.CategoryPath));
        paths.AddRange(listings.Select(x => x.MakerId).Distinct().Order(StringComparer.Ordinal).Select(Seo.MakerPath));
        paths.AddRange(listings.Select(x => Seo.ProductPath(x.Id)));

        var totalParts = (paths.Count - 1) / SitemapSize + 1;
        if (part.HasValue && (part.Value < 1 || part.Value > totalParts || totalParts == 1)) return NotFound();
        XElement Location(string path) => new(SitemapNamespace + "loc", Seo.Absolute(path));

        var root = part is null && totalParts > 1
            ? new XElement(SitemapNamespace + "sitemapindex", Enumerable.Range(1, totalParts)
                .Select(i => new XElement(SitemapNamespace + "sitemap", Location(FormattableString.Invariant($"/sitemap-{i}.xml")))))
            : new XElement(SitemapNamespace + "urlset", paths.Skip(((part ?? 1) - 1) * SitemapSize).Take(SitemapSize)
                .Select(path => new XElement(SitemapNamespace + "url", Location(path))));

        return Content("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + root.ToString(SaveOptions.DisableFormatting),
            "application/xml; charset=utf-8");
    }
}
