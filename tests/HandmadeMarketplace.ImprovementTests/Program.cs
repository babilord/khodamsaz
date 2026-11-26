using HandmadeMarketplace.Application;
using HandmadeMarketplace.Domain;
using HandmadeMarketplace.Infrastructure;
using HandmadeMarketplace.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Globalization;

var checks = 0;
void Check(bool value, string label) {
    if (!value) throw new Exception("FAIL: " + label);
    Console.WriteLine("PASS: " + label); checks++;
}
foreach (var input in new[] { "7,000,000", "۷٬۰۰۰٬۰۰۰", "٧٬٠٠٠٬٠٠٠", "۷0٠۰۰۰۰", " 7000000 " })
    Check(PersianPrice.TryParse(input, out var value) && value == 7000000, "normalize " + input);
foreach (var input in new[] { "", " ", "-1", "1.5", "۱e۳", "12x", "9223372036854775808", "۱ ۲" })
    Check(!PersianPrice.TryParse(input, out _), "reject " + input);
Check(PersianPrice.Format(7000000) == "۷٬۰۰۰٬۰۰۰", "Persian grouping");
Check(PersianPrice.Format(0) == "۰", "zero formatting");
var beforeTehranMidnight = new DateTime(2026, 10, 29, 20, 29, 59, DateTimeKind.Utc);
Check(UiText.JalaliDate(UiText.ListingDate(beforeTehranMidnight)) == "۱۴۰۵/۰۸/۰۷", "listing date before Tehran midnight");
Check(UiText.JalaliDate(UiText.ListingDate(beforeTehranMidnight.AddSeconds(1))) == "۱۴۰۵/۰۸/۰۸", "listing date after Tehran midnight");
var metadata = new EmptyModelMetadataProvider();
async Task<DefaultModelBindingContext> Bind(string value, Type type) {
    var action = new ActionContext { HttpContext = new DefaultHttpContext() };
    var provider = new QueryStringValueProvider(BindingSource.Query,
        new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["Price"] = value }), CultureInfo.InvariantCulture);
    var context = (DefaultModelBindingContext)DefaultModelBindingContext.CreateBindingContext(action, provider, metadata.GetMetadataForType(type), null, "Price");
    await new PriceModelBinder().BindModelAsync(context); return context;
}
Check((long)(await Bind("۷٬۰۰۰٬۰۰۰", typeof(long))).Result.Model! == 7000000, "MVC binds localized price");
Check((await Bind("", typeof(long?))).Result.IsModelSet && (await Bind("", typeof(long?))).Result.Model == null, "empty optional filter");
Check(!(await Bind("", typeof(long))).ModelState.IsValid, "empty required price rejected");
Check(!(await Bind("1000000000001", typeof(long))).ModelState.IsValid, "maximum enforced");
Check((long)(await Bind("۰", typeof(long))).Result.Model! == 0, "negotiable zero accepted");

var connection = Environment.GetEnvironmentVariable("IMPROVEMENT_TEST_CONNECTION");
if (string.IsNullOrEmpty(connection)) {
    Console.WriteLine($"{checks} checks passed. SKIP: PostgreSQL behavior checks require IMPROVEMENT_TEST_CONNECTION.");
    return;
}
// Always create a unique disposable database; never migrate or delete the supplied database.
var builder = new Npgsql.NpgsqlConnectionStringBuilder(connection) { Database = "handmade_improvements_" + Guid.NewGuid().ToString("N") };
await using var db = new MarketplaceDb(new DbContextOptionsBuilder<MarketplaceDb>().UseNpgsql(builder.ConnectionString).Options);
try {
    await db.GetService<IMigrator>().MigrateAsync("20261025173948_InitialPostgreSql");
    await db.Database.ExecuteSqlRawAsync("""
        INSERT INTO "AspNetUsers" ("Id", "UserName", "DisplayName", "City", "Bio", "PhoneNumber",
            "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
        VALUES ('maker', 'maker', '', '', '', '09123456789', FALSE, FALSE, FALSE, FALSE, 0),
               ('buyer', 'buyer', '', '', '', '+989123456789', FALSE, FALSE, FALSE, FALSE, 0),
               ('third', 'third', '', '', '', NULL, FALSE, FALSE, FALSE, FALSE, 0)
        """);
    var category = new Category { Name = "Test" }; db.Categories.Add(category);
    var listing = new Listing { Title = "Test", MakerId = "maker", Category = category, Status = ListingStatus.Approved };
    var second = new Listing { Title = "Other", MakerId = "maker", Category = category, Status = ListingStatus.Approved };
    db.Listings.AddRange(listing, second); await db.SaveChangesAsync();
    // Insert a pre-upgrade row without referring to the not-yet-created IsRead column.
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Messages\" (\"ListingId\", \"SenderId\", \"RecipientId\", \"Body\", \"SentAt\") VALUES ({listing.Id}, {"buyer"}, {"maker"}, {"existing message"}, {DateTime.UtcNow})");
    await db.Database.MigrateAsync();
    Check(await db.Users.CountAsync(x => x.PhoneNumber != null && !x.PhoneNumberConfirmed && x.VerifiedMobile == null) == 2,
        "migration preserves duplicate legacy phones without verifying them");
    var preserved = await db.Messages.SingleAsync();
    Check(preserved.Body == "existing message" && preserved.SenderId == "buyer" && preserved.RecipientId == "maker" && preserved.ListingId == listing.Id, "migration preserves existing message content and relationships");
    Check(!preserved.IsRead, "migration initializes existing messages as unread");
    Check(!db.Database.HasPendingModelChanges(), "migration matches current model");
    db.Messages.Remove(preserved); await db.SaveChangesAsync();
    var service = new MarketplaceService(db);
    await service.Send(listing.Id, "buyer", "maker", "first");
    var first = await db.Messages.MaxAsync(x => x.Id);
    await service.Send(listing.Id, "buyer", "maker", "arrived later");
    await service.Send(second.Id, "buyer", "maker", "other conversation");
    await service.Send(listing.Id, "maker", "buyer", "reply");
    Check(await service.UnreadCount("maker") == 3, "incoming count excludes outgoing");
    Check(await service.UnreadCount("buyer") == 1, "recipient counts isolated");
    Check(await service.UnreadCount("third") == 0, "third party count isolated");
    await service.Thread(listing.Id, "maker", "buyer");
    Check(await service.UnreadCount("maker") == 3, "fetch does not mark unread messages");
    await service.MarkRead(listing.Id, "third", "buyer", int.MaxValue);
    Check(await service.UnreadCount("maker") == 3, "third party cannot mark recipient messages");
    await service.MarkRead(listing.Id, "maker", "buyer", first);
    Check(await service.UnreadCount("maker") == 2, "only rendered messages acknowledged; later arrivals preserved");
    await service.MarkRead(listing.Id, "maker", "buyer", first);
    Check(await service.UnreadCount("maker") == 2, "read acknowledgement idempotent");
    await service.MarkRead(listing.Id, "maker", "buyer", int.MaxValue);
    Check(await service.UnreadCount("maker") == 1, "other conversation remains unread");
    Check(await service.UnreadCount("buyer") == 1, "outgoing messages never marked for other recipient");
    await service.MarkRead(second.Id, "maker", "buyer", int.MaxValue);
    Check(await service.UnreadCount("maker") == 0, "all incoming read");
    Check((await service.Thread(listing.Id, "third", "buyer")).Count == 0, "third party cannot read conversation");
    var purchases = new PurchaseDeclarationService(db);
    for (var count = 1; count <= PurchaseDeclaration.Limit; count++)
    {
        var result = await purchases.Declare(listing.Id, "buyer", Guid.NewGuid(), default);
        Check(result.Outcome == PurchaseOutcome.Recorded &&
            result.Statistics == new PurchaseStatistics(1, count, count), $"purchase declaration {count} succeeds");
    }
    await db.PurchaseDeclarations.ExecuteUpdateAsync(setters =>
        setters.SetProperty(x => x.CreatedAt, DateTime.UtcNow.AddYears(-5)));
    var aged = await purchases.Declare(listing.Id, "buyer", Guid.NewGuid(), default);
    Check(aged.Outcome == PurchaseOutcome.LimitReached, "purchase limit includes declarations from five years ago");
    Check((await purchases.Declare(second.Id, "buyer", Guid.NewGuid(), default)).Outcome ==
        PurchaseOutcome.Recorded, "purchase limit is independent for another product by the same seller");
    await MobileOtpChecks.Run(builder.ConnectionString, Check);
    Console.WriteLine($"All {checks} checks passed.");
} finally { await db.Database.EnsureDeletedAsync(); }
