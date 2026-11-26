using HandmadeMarketplace.Application;
using HandmadeMarketplace.Domain;
using Microsoft.EntityFrameworkCore;
namespace HandmadeMarketplace.Infrastructure;
public class MarketplaceService(MarketplaceDb db) : IMarketplace
{
    private IQueryable<Listing> Listings => db.Listings.Include(x => x.Category).Include(x => x.Images);
    public async Task<IReadOnlyList<Category>> Categories() => await db.Categories.AsNoTracking().OrderBy(x => x.ParentId).ThenBy(x => x.Name).ToListAsync();
    public async Task<SearchResult> Search(Search s)
    {
        var q = Listings.AsNoTracking().Where(x => x.Status == ListingStatus.Approved);
        if (!string.IsNullOrWhiteSpace(s.Q))
        {
            // Keep case-insensitive literal substring search after moving from SQL Server collation.
            var pattern = "%" + s.Q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            q = q.Where(x => EF.Functions.ILike(x.Title, pattern, "\\") || EF.Functions.ILike(x.Description, pattern, "\\"));
        }
        if (s.CategoryId.HasValue) q = q.Where(x => x.CategoryId == s.CategoryId || x.Category.ParentId == s.CategoryId);
        if (!string.IsNullOrWhiteSpace(s.City)) q = q.Where(x => x.City == s.City);
        if (s.Min.HasValue) q = q.Where(x => x.Price >= s.Min);
        if (s.Max.HasValue) q = q.Where(x => x.Price <= s.Max);
        var count = await q.CountAsync();
        var page = Math.Clamp(s.Page, 1, Math.Max(1, (int)Math.Ceiling(count / (double)SearchResult.PageSize)));
        var ordered = s.Sort switch
        {
            "price-asc" => q.OrderBy(x => x.Price).ThenBy(x => x.Id),
            "price-desc" => q.OrderByDescending(x => x.Price).ThenByDescending(x => x.Id),
            "oldest" => q.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            _ => q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
        };
        return new(await ordered.Skip((page - 1) * SearchResult.PageSize).Take(SearchResult.PageSize).ToListAsync(), count, page);
    }
    public Task<Listing?> Get(int id) => Listings.FirstOrDefaultAsync(x => x.Id == id);
    public async Task<IReadOnlyList<Listing>> Mine(string userId) => await Listings.Where(x => x.MakerId == userId).OrderByDescending(x => x.CreatedAt).ToListAsync();
    public async Task<int> Save(ListingInput input, string userId, IReadOnlyList<string> images, int[] removeImages)
    {
        if (!await db.Categories.AnyAsync(x => x.Id == input.CategoryId)) throw new ArgumentException("دسته‌بندی معتبر نیست");
        var listing = input.Id == 0 ? new Listing { MakerId = userId } : await Get(input.Id) ?? throw new KeyNotFoundException();
        if (listing.MakerId != userId) throw new UnauthorizedAccessException();
        var removed = listing.Images.Where(x => removeImages.Contains(x.Id)).ToList();
        if (listing.Images.Count - removed.Count + images.Count > 6) throw new ArgumentException("حداکثر شش تصویر مجاز است");
        foreach (var image in removed) { listing.Images.Remove(image); db.Remove(image); }
        listing.Title = input.Title.Trim(); listing.Description = input.Description.Trim(); listing.City = input.City.Trim();
        listing.Price = input.Price; listing.CategoryId = input.CategoryId; listing.Status = ListingStatus.Pending;
        listing.Images.AddRange(images.Select(x => new ListingImage { Path = x }));
        if (input.Id == 0) db.Listings.Add(listing);
        await db.SaveChangesAsync(); return listing.Id;
    }
    public async Task<bool> Delete(int id, string userId)
    {
        var item = await Get(id); if (item == null || item.MakerId != userId) return false;
        db.Listings.Remove(item); await db.SaveChangesAsync(); return true;
    }
    public async Task<bool> MarkSold(int id, string userId)
    {
        var item = await Get(id); if (item == null || item.MakerId != userId) return false;
        item.Status = ListingStatus.Sold; await db.SaveChangesAsync(); return true;
    }
    public async Task<IReadOnlyList<Listing>> Favorites(string userId) => await Listings.Where(x => x.Status == ListingStatus.Approved && db.Favorites.Any(f => f.UserId == userId && f.ListingId == x.Id)).ToListAsync();
    public async Task ToggleFavorite(int id, string userId)
    {
        var f = await db.Favorites.FindAsync(userId, id);
        if (f != null) db.Remove(f);
        else if (await db.Listings.AnyAsync(x => x.Id == id && x.Status == ListingStatus.Approved)) db.Favorites.Add(new() { UserId = userId, ListingId = id });
        await db.SaveChangesAsync();
    }
    public async Task<IReadOnlyList<Listing>> Moderation() => await Listings.OrderBy(x => x.Status).ThenByDescending(x => x.CreatedAt).Take(200).ToListAsync();
    public async Task Moderate(int id, bool approve) { var l = await Get(id); if (l != null) { l.Status = approve ? ListingStatus.Approved : ListingStatus.Rejected; await db.SaveChangesAsync(); } }
    public async Task<MakerProfile?> Maker(string id) => await db.Users.Where(x => x.Id == id).Select(x => new MakerProfile(x.Id, x.DisplayName, x.City, x.Bio)).FirstOrDefaultAsync();
    public async Task<IReadOnlyList<Listing>> ByMaker(string id) => await Listings.Where(x => x.MakerId == id && x.Status == ListingStatus.Approved).ToListAsync();
    public async Task<IReadOnlyList<Message>> Inbox(string userId) => await db.Messages.Include(x => x.Listing).Where(x => x.SenderId == userId || x.RecipientId == userId).OrderByDescending(x => x.SentAt).ToListAsync();
    public async Task<IReadOnlyList<Message>> Thread(int listingId, string userId, string otherId) => await db.Messages.Where(x => x.ListingId == listingId && ((x.SenderId == userId && x.RecipientId == otherId) || (x.SenderId == otherId && x.RecipientId == userId))).OrderBy(x => x.SentAt).ToListAsync();
    public Task<int> UnreadCount(string userId) => db.Messages.CountAsync(x => x.RecipientId == userId && x.SenderId != userId && !x.IsRead);
    public Task MarkRead(int listingId, string userId, string otherId, int throughId) => db.Messages
        .Where(x => x.ListingId == listingId && x.RecipientId == userId && x.SenderId == otherId && x.Id <= throughId && !x.IsRead)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsRead, true));
    public async Task Send(int listingId, string senderId, string recipientId, string body)
    {
        var listing = await Get(listingId) ?? throw new ArgumentException("آگهی پیدا نشد");
        if (senderId == recipientId || (senderId != listing.MakerId && recipientId != listing.MakerId) || !await db.Users.AnyAsync(x => x.Id == recipientId)) throw new ArgumentException("گیرنده معتبر نیست");
        if (listing.Status != ListingStatus.Approved && !await db.Messages.AnyAsync(x => x.ListingId == listingId && ((x.SenderId == senderId && x.RecipientId == recipientId) || (x.SenderId == recipientId && x.RecipientId == senderId)))) throw new ArgumentException("آگهی قابل گفتگو نیست");
        if (string.IsNullOrWhiteSpace(body) || body.Length > 2000) throw new ArgumentException("متن پیام باید بین ۱ تا ۲۰۰۰ نویسه باشد");
        db.Messages.Add(new() { ListingId = listingId, SenderId = senderId, RecipientId = recipientId, Body = body.Trim() }); await db.SaveChangesAsync();
    }
}
