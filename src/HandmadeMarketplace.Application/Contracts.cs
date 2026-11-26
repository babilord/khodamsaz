using System.ComponentModel.DataAnnotations;
using HandmadeMarketplace.Domain;
namespace HandmadeMarketplace.Application;

public record Search(string? Q = null, int? CategoryId = null, string? City = null, long? Min = null, long? Max = null, int Page = 1, string? Sort = null);
public record SearchResult(IReadOnlyList<Listing> Items, int Total, int Page)
{
    public const int PageSize = 12;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
    // Always show the endpoints and a small window around the current page.
    public IEnumerable<int?> PageNumbers()
    {
        var previous = 0;
        for (var n = 1; n <= TotalPages; n++)
        {
            if (n > 2 && n < TotalPages - 1 && Math.Abs(n - Page) > 1) continue;
            if (n > previous + 1) yield return null;
            yield return n;
            previous = n;
        }
    }
}
public record MakerProfile(string Id, string Name, string City, string Bio);
public class ListingInput
{
    public int Id { get; set; }
    public const string DescriptionLengthMessage = "لطفاً توضیحات محصول را بین ۱۰ تا ۴۰۰۰ کاراکتر وارد کنید.";
    public const string PriceMessage = "قیمت باید عددی بین صفر و ۱٬۰۰۰٬۰۰۰٬۰۰۰٬۰۰۰ تومان باشد؛ برای قیمت توافقی صفر وارد کنید.";
    public const string CategoryMessage = "لطفاً یک دسته‌بندی معتبر انتخاب کنید.";

    [Required(ErrorMessage = "عنوان را وارد کنید"), StringLength(120, ErrorMessage = "عنوان محصول نباید بیشتر از ۱۲۰ کاراکتر باشد.")]
    public string Title { get; set; } = "";

    [Required(ErrorMessage = "لطفاً توضیحات محصول را وارد کنید.")]
    [StringLength(4000, MinimumLength = 10, ErrorMessage = DescriptionLengthMessage)]
    public string Description { get; set; } = "";

    [Required(ErrorMessage = "لطفاً شهر را وارد کنید."), StringLength(80, ErrorMessage = "نام شهر نباید بیشتر از ۸۰ کاراکتر باشد.")]
    public string City { get; set; } = "";

    [Required(ErrorMessage = PriceMessage), Range(0, 1000000000000, ErrorMessage = PriceMessage)]
    public long Price { get; set; }

    [Required(ErrorMessage = CategoryMessage), Range(1, int.MaxValue, ErrorMessage = CategoryMessage)]
    public int CategoryId { get; set; }
}
public interface IMarketplace
{
    Task<IReadOnlyList<Category>> Categories();
    Task<SearchResult> Search(Search search);
    Task<Listing?> Get(int id);
    Task<IReadOnlyList<Listing>> Mine(string userId);
    Task<int> Save(ListingInput input, string userId, IReadOnlyList<string> images, int[] removeImages);
    Task<bool> Delete(int id, string userId);
    Task<bool> MarkSold(int id, string userId);
    Task<IReadOnlyList<Listing>> Favorites(string userId);
    Task ToggleFavorite(int id, string userId);
    Task<IReadOnlyList<Listing>> Moderation();
    Task Moderate(int id, bool approve);
    Task<MakerProfile?> Maker(string id);
    Task<IReadOnlyList<Listing>> ByMaker(string id);
    Task<IReadOnlyList<Message>> Inbox(string userId);
    Task<IReadOnlyList<Message>> Thread(int listingId, string userId, string otherId);
    Task<int> UnreadCount(string userId);
    Task MarkRead(int listingId, string userId, string otherId, int throughId);
    Task Send(int listingId, string senderId, string recipientId, string body);
}
