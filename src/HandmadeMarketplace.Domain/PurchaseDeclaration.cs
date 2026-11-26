namespace HandmadeMarketplace.Domain;

public sealed class PurchaseDeclaration
{
    public const int Limit = 10;

    public int ProductId { get; set; }
    public string UserId { get; set; } = "";
    public Guid RequestId { get; set; }
    public int Number { get; set; }
    public DateTime CreatedAt { get; set; }
}
