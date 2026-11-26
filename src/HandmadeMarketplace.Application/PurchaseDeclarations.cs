namespace HandmadeMarketplace.Application;

public sealed record PurchaseStatistics(long UniqueBuyers = 0, long Total = 0, int PersonalCount = 0);

public enum PurchaseOutcome { Recorded, LimitReached, NotFound, OwnProduct }

public sealed record PurchaseResult(PurchaseOutcome Outcome, PurchaseStatistics? Statistics = null);

public interface IPurchaseDeclarations
{
    Task<Dictionary<int, PurchaseStatistics>> GetStatistics(
        int[] productIds, string? userId, CancellationToken cancellationToken);
    Task<PurchaseResult> Declare(int productId, string userId, Guid requestId, CancellationToken cancellationToken);
}
