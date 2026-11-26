using HandmadeMarketplace.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace HandmadeMarketplace.Infrastructure;
public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public string City { get; set; } = "";
    public string Bio { get; set; } = "";
}
public class MarketplaceDb(DbContextOptions<MarketplaceDb> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<PurchaseDeclaration> PurchaseDeclarations => Set<PurchaseDeclaration>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Category>().HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Category>().Property(x => x.Name).HasMaxLength(100);
        b.Entity<Listing>().Property(x => x.Title).HasMaxLength(120);
        b.Entity<Listing>().Property(x => x.Description).HasMaxLength(4000);
        b.Entity<Listing>().Property(x => x.City).HasMaxLength(80);
        b.Entity<Listing>().HasIndex(x => new { x.Status, x.CreatedAt });
        b.Entity<Listing>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.MakerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Listing>().HasOne(x => x.Category).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<Listing>().ToTable(t => t.HasCheckConstraint("CK_Listing_Price", "\"Price\" >= 0"));
        b.Entity<Favorite>().HasKey(x => new { x.UserId, x.ListingId });
        b.Entity<Favorite>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Message>().HasIndex(x => new { x.RecipientId, x.IsRead });
        b.Entity<Message>().Property(x => x.Body).HasMaxLength(2000);
        b.Entity<Message>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Message>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.RecipientId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PurchaseDeclaration>(declaration =>
        {
            declaration.HasKey(x => new { x.ProductId, x.UserId, x.RequestId });
            declaration.HasIndex(x => new { x.ProductId, x.UserId, x.Number }).IsUnique();
            declaration.HasOne<Listing>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            declaration.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            declaration.ToTable(t => t.HasCheckConstraint("CK_PurchaseDeclaration_Number",
                $"\"Number\" BETWEEN 1 AND {PurchaseDeclaration.Limit}"));
        });
    }
}
