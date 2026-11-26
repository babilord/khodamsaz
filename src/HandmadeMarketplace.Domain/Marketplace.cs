namespace HandmadeMarketplace.Domain;

public enum ListingStatus { Pending, Approved, Rejected, Sold }
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
}
public class Listing
{
    public int Id { get; set; }
    public string MakerId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string City { get; set; } = "";
    public long Price { get; set; }
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public ListingStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<ListingImage> Images { get; set; } = [];
}
public class ListingImage
{
    public int Id { get; set; }
    public int ListingId { get; set; }
    public string Path { get; set; } = "";
}
public class Favorite
{
    public string UserId { get; set; } = "";
    public int ListingId { get; set; }
    public Listing Listing { get; set; } = null!;
}
public class Message
{
    public int Id { get; set; }
    public int ListingId { get; set; }
    public Listing Listing { get; set; } = null!;
    public string SenderId { get; set; } = "";
    public string RecipientId { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsRead { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
