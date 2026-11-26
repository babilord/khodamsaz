using HandmadeMarketplace.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace HandmadeMarketplace.Infrastructure;
public static class DatabaseSeed
{
    public static async Task Run(IServiceProvider services, IConfiguration configuration)
    {
        var db = services.GetRequiredService<MarketplaceDb>();
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(51802026)");
        if (!await db.Categories.AnyAsync()) {
            string[] roots = ["زیورآلات", "خوراکی خانگی", "چوب و دکور", "ربات و الکترونیک", "پوشاک", "هنر و نقاشی"];
            foreach (var name in roots) db.Categories.Add(new() { Name = name }); await db.SaveChangesAsync();
            var parents = await db.Categories.OrderBy(x => x.Id).ToListAsync();
            string[] children = ["گردنبند و دستبند", "کیک و شیرینی", "وسایل چوبی", "ربات آموزشی", "لباس دست‌دوز", "نقاشی و تصویرسازی"];
            for (int i = 0; i < parents.Count; i++) db.Categories.Add(new() { Name = children[i], ParentId = parents[i].Id }); await db.SaveChangesAsync();
        }

        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        var users = services.GetRequiredService<UserManager<AppUser>>();
        if (!await roles.RoleExistsAsync("Admin")) Require(await roles.CreateAsync(new("Admin")));
        var email = configuration["Seed:AdminEmail"];
        var password = configuration["Seed:AdminPassword"];
        if (!string.IsNullOrWhiteSpace(email))
        {
            var admin = await users.FindByEmailAsync(email);
            if (admin == null)
            {
                if (string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("Seed:AdminPassword is required for a new administrator.");
                admin = new() { UserName = email, Email = email, DisplayName = "Admin", EmailConfirmed = true };
                Require(await users.CreateAsync(admin, password));
            }
            if (!await users.IsInRoleAsync(admin, "Admin")) Require(await users.AddToRoleAsync(admin, "Admin"));
        }
        await transaction.CommitAsync();
        });
    }
    private static void Require(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(string.Join(", ", result.Errors.Select(x => x.Description)));
    }
}
