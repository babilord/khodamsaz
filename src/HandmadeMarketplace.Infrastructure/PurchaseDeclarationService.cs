using HandmadeMarketplace.Application;
using HandmadeMarketplace.Domain;
using Microsoft.EntityFrameworkCore;

namespace HandmadeMarketplace.Infrastructure;

public sealed class PurchaseDeclarationService(MarketplaceDb db) : IPurchaseDeclarations
{
    public Task<Dictionary<int, PurchaseStatistics>> GetStatistics(
        int[] productIds, string? userId, CancellationToken cancellationToken)
    {
        if (productIds.Length == 0)
            return Task.FromResult(new Dictionary<int, PurchaseStatistics>());

        return db.PurchaseDeclarations.Where(x => productIds.Contains(x.ProductId))
            .GroupBy(x => x.ProductId)
            .Select(group => new
            {
                ProductId = group.Key,
                UniqueBuyers = group.Select(x => x.UserId).Distinct().LongCount(),
                Total = group.LongCount(),
                PersonalCount = group.Count(x => x.UserId == userId)
            })
            .ToDictionaryAsync(x => x.ProductId,
                x => new PurchaseStatistics(x.UniqueBuyers, x.Total, x.PersonalCount), cancellationToken);
    }

    public Task<PurchaseResult> Declare(int productId, string userId, Guid requestId, CancellationToken cancellationToken)
        => db.Database.CreateExecutionStrategy().ExecuteAsync(async cancellationToken =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.ReadCommitted, cancellationToken);

            // Serialize declarations and listing edits on the product row until commit.
            var products = await db.Listings.FromSqlInterpolated(
                    $"""SELECT * FROM "Listings" WHERE "Id" = {productId} FOR UPDATE""")
                .AsNoTracking().ToListAsync(cancellationToken);
            var product = products.SingleOrDefault();
            if (product is null || product.Status != ListingStatus.Approved)
                return new PurchaseResult(PurchaseOutcome.NotFound);
            if (product.MakerId == userId)
                return new PurchaseResult(PurchaseOutcome.OwnProduct);

            var previous = await db.PurchaseDeclarations
                .Where(x => x.ProductId == productId && x.UserId == userId)
                .Select(x => x.RequestId).ToListAsync(cancellationToken);
            var outcome = PurchaseOutcome.Recorded;

            if (!previous.Contains(requestId))
            {
                if (previous.Count >= PurchaseDeclaration.Limit)
                    outcome = PurchaseOutcome.LimitReached;
                else
                {
                    // Writing directly keeps the change tracker clear if the execution strategy retries.
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO "PurchaseDeclarations" ("ProductId", "UserId", "RequestId", "Number", "CreatedAt")
                        VALUES ({productId}, {userId}, {requestId}, {previous.Count + 1}, {DateTime.UtcNow})
                        """, cancellationToken);
                }
            }

            var statistics = await GetStatistics([productId], userId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new PurchaseResult(outcome, statistics[productId]);
        }, cancellationToken);
}
