using System.Text.RegularExpressions;
using HandmadeMarketplace.Domain;
using Microsoft.AspNetCore.Http;

namespace HandmadeMarketplace.Web.Models;

public sealed record SeoPage(string Title, string Description, string? Path = null,
    bool Index = true, string? Image = null, object? Schema = null, string Type = "website");

public static class Seo
{
    public const string Brand = "خودم‌ساز";
    public const string Origin = "https://khodamsaz.ir";
    public const string Qazvin = "قزوین";
    public const string QazvinPath = "/qazvin";
    public const string HomeTitle = Brand + " | خرید و فروش محصولات دست‌ساز و صنایع دستی";
    public const string HomeDescription = "در خودم‌ساز محصولات دست‌ساز، صنایع دستی و آثار هنری سازندگان مستقل را کشف کنید. آگهی‌ها را ببینید و مستقیماً با سازنده در ارتباط باشید.";

    public static string Absolute(string path) => Origin + path;
    public static string ProductPath(int id) => FormattableString.Invariant($"/Listings/Details/{id}");
    public static string CategoryPath(int id) => FormattableString.Invariant($"/categories/{id}");
    public static string MakerPath(string id) => "/Listings/Maker/" + Uri.EscapeDataString(id);
    public static string PagePath(string path, int page) => page <= 1 ? path : FormattableString.Invariant($"{path}?page={page}");
    public static string PageTitle(string title, int page) => page <= 1 ? title : $"{title} | صفحه {Application.PersianPrice.Format(page)}";

    public static bool CleanQuery(IQueryCollection query, int? page = null) =>
        query.Count == 0 || (page is > 0 && query.Count == 1 && query.TryGetValue("page", out var values)
            && values.Count == 1 && values[0] == page.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static string Summary(string? value, string fallback, int length = 160)
    {
        var text = Regex.Replace(value ?? "", @"\s+", " ").Trim();
        if (text.Length == 0) return fallback;
        if (text.Length <= length) return text;
        var boundary = text.LastIndexOf(' ', length - 1);
        if (boundary < length / 2) boundary = length - 1;
        if (char.IsHighSurrogate(text[boundary - 1])) boundary--;
        return text[..boundary].TrimEnd() + "…";
    }

    public static string? ProductImage(string path) => path.Length > 9 && path.StartsWith("/uploads/", StringComparison.Ordinal)
        && Path.GetFileName(path) == path[9..] ? Absolute("/uploads/" + Uri.EscapeDataString(path[9..])) : null;

    public static object HomeSchema() => new Dictionary<string, object>
    {
        ["@context"] = "https://schema.org",
        ["@graph"] = new object[]
        {
            new Dictionary<string, object> { ["@type"] = "Organization", ["@id"] = Origin + "/#organization",
                ["name"] = Brand, ["url"] = Origin + "/" },
            new Dictionary<string, object> { ["@type"] = "WebSite", ["@id"] = Origin + "/#website",
                ["name"] = Brand, ["alternateName"] = new[] { "خودم ساز", "خودمساز" }, ["url"] = Origin + "/",
                ["inLanguage"] = "fa-IR", ["publisher"] = new Dictionary<string, string> { ["@id"] = Origin + "/#organization" } }
        }
    };

    public static object Breadcrumbs(params (string Name, string Path)[] crumbs) => new Dictionary<string, object>
    {
        ["@context"] = "https://schema.org", ["@type"] = "BreadcrumbList",
        ["itemListElement"] = crumbs.Select((crumb, index) => new Dictionary<string, object>
            { ["@type"] = "ListItem", ["position"] = index + 1, ["name"] = crumb.Name, ["item"] = Absolute(crumb.Path) })
    };

    public static SeoPage Product(Listing listing, bool canonicalRequest)
    {
        var path = ProductPath(listing.Id);
        var title = $"{listing.Title} | {Brand}";
        var description = Summary(listing.Description, $"{listing.Title}، {listing.Category.Name} در {listing.City}.");
        if (listing.Status != ListingStatus.Approved) return new(title, description, Index: false);
        var images = listing.Images.Select(x => ProductImage(x.Path)).OfType<string>().ToArray();
        var product = new Dictionary<string, object>
        {
            ["@context"] = "https://schema.org", ["@type"] = "Product", ["name"] = listing.Title,
            ["description"] = description, ["url"] = Absolute(path), ["category"] = listing.Category.Name
        };
        if (images.Length > 0) product["image"] = images;
        if (listing.Price > 0)
            product["offers"] = new Dictionary<string, object> { ["@type"] = "Offer", ["url"] = Absolute(path),
                ["priceCurrency"] = "IRR", ["price"] = listing.Price * 10m };
        return new(title, description, path, canonicalRequest, images.FirstOrDefault(),
            new[] { product, Breadcrumbs((Brand, "/"), (listing.Category.Name, CategoryPath(listing.CategoryId)),
                (listing.Title, path)) }, "product");
    }
}
