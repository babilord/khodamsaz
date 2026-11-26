using HandmadeMarketplace.Application;
using HandmadeMarketplace.Domain;
using HandmadeMarketplace.Infrastructure;
using Microsoft.EntityFrameworkCore;

// A uniquely named, disposable database: never use the development connection string.
var name = "handmade_pagination_" + Guid.NewGuid().ToString("N");
var testConnection = Environment.GetEnvironmentVariable("PAGINATION_TEST_CONNECTION")
    ?? throw new InvalidOperationException("Set PAGINATION_TEST_CONNECTION to a PostgreSQL connection with CREATEDB permission.");
var connection = new Npgsql.NpgsqlConnectionStringBuilder(testConnection) { Database = name };
var commands = new List<string>();
var options = new DbContextOptionsBuilder<MarketplaceDb>()
    .UseNpgsql(connection.ConnectionString)
    .LogTo(commands.Add, new[] { Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted })
    .Options;
await using var db = new MarketplaceDb(options);
var checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAIL: " + label);
    checks++;
}
try
{
    await db.Database.MigrateAsync();
    db.Users.Add(new AppUser { Id = "maker", UserName = "maker" });
    var parent = new Category { Name = "Parent" };
    var child = new Category { Name = "Child", Parent = parent };
    db.Categories.AddRange(parent, child);
    await db.SaveChangesAsync();
    var timestamp = DateTime.UtcNow;
    for (var i = 1; i <= 600; i++)
        db.Listings.Add(new Listing { Title = "Product " + i, Description = "Handmade test", MakerId = "maker", CategoryId = child.Id,
            City = i % 2 == 0 ? "A" : "B", Price = i, CreatedAt = timestamp, Status = ListingStatus.Approved,
            Images = [new ListingImage { Path = "/test.png" }] });
    foreach (var status in new[] { ListingStatus.Pending, ListingStatus.Rejected, ListingStatus.Sold })
        db.Listings.Add(new Listing { Title = "Product hidden", MakerId = "maker", CategoryId = child.Id, City = "A", Price = 200, Status = status });
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    var service = new MarketplaceService(db);
    commands.Clear();
    var seen = new HashSet<int>();
    for (var page = 1; page <= 50; page++)
    {
        var result = await service.Search(new(Page: page));
        Check(result.Total == 600 && result.TotalPages == 50 && result.Page == page && result.Items.Count == 12, "50 full pages");
        Check(result.Items.All(x => x.Status == ListingStatus.Approved && x.Images.Count == 1 && seen.Add(x.Id)), "unique approved items with images");
    }
    Check(seen.Count == 600, "every listing appears exactly once");
    Check(commands.Count == 100 && commands.Count(x => x.Contains("OFFSET") && x.Contains("LIMIT")) == 50
        && commands.Count(x => x.Contains("count(*)", StringComparison.OrdinalIgnoreCase)) == 50, "SQL count plus bounded page query per request");
    Check(db.ChangeTracker.Entries().Count() == 0, "public queries do not track entities");
    foreach (var page in new[] { int.MinValue, -1, 0, 51, int.MaxValue })
        Check((await service.Search(new(Page: page))).Page == (page < 1 ? 1 : 50), "invalid page clamped");
    foreach (var sort in new[] { "newest", "oldest", "price-asc", "price-desc", "invalid" })
    {
        var result = await service.Search(new(Sort: sort));
        var ascending = sort is "oldest" or "price-asc";
        Check(result.Items.Select(x => x.Price).SequenceEqual(ascending ? Enumerable.Range(1, 12).Select(x => (long)x) : Enumerable.Range(589, 12).Reverse().Select(x => (long)x)), "deterministic sorting " + sort);
    }
    var filtered = await service.Search(new(Q: "Product", CategoryId: parent.Id, City: "A", Min: 100, Max: 200, Page: 2, Sort: "price-asc"));
    Check(filtered.Total == 51 && filtered.TotalPages == 5 && filtered.Items.Count == 12 && filtered.Items[0].Price == 124, "all filters precede pagination, including child category");
    Check((await service.Search(new(Q: "product"))).Total == 600, "case-insensitive PostgreSQL search");
    Check((await service.Search(new(Q: "%_"))).Total == 0, "search treats wildcard characters literally");
    var empty = await service.Search(new(Q: "absent", Page: int.MaxValue));
    Check(empty.Total == 0 && empty.Page == 1 && empty.Items.Count == 0, "empty results");
    Check(new SearchResult([], 600, 1).PageNumbers().SequenceEqual(new int?[] { 1, 2, null, 49, 50 }), "first page ellipsis");
    Check(new SearchResult([], 600, 25).PageNumbers().SequenceEqual(new int?[] { 1, 2, null, 24, 25, 26, null, 49, 50 }), "middle ellipses");
    Check(new SearchResult([], 600, 50).PageNumbers().SequenceEqual(new int?[] { 1, 2, null, 49, 50 }), "last page ellipsis");
    Console.WriteLine($"All {checks} pagination checks passed (600 listings, 50 pages; SQL LIMIT/OFFSET verified).");
}
finally
{
    await db.Database.EnsureDeletedAsync();
    Console.WriteLine("Deleted isolated pagination test database: " + name);
}

