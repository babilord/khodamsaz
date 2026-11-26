using HandmadeMarketplace.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace HandmadeMarketplace.Infrastructure;
public static class DevelopmentSeed
{
    public static async Task Run(IServiceProvider services, string password)
    {
        var db = services.GetRequiredService<MarketplaceDb>(); await db.Database.MigrateAsync();
        var users = services.GetRequiredService<UserManager<AppUser>>();
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync("Admin")) await roles.CreateAsync(new("Admin"));
        async Task<AppUser> User(string email, string name, string city, bool admin = false) {
            var u = await users.FindByEmailAsync(email);
            if (u == null) { u = new() { UserName = email, Email = email, DisplayName = name, City = city, Bio = "با عشق می‌سازم؛ پذیرای سفارش‌های اختصاصی شما هستم.", EmailConfirmed = true }; var r = await users.CreateAsync(u, password); if (!r.Succeeded) throw new InvalidOperationException(string.Join(",", r.Errors.Select(x => x.Description))); }
            if (admin && !await users.IsInRoleAsync(u, "Admin")) await users.AddToRoleAsync(u, "Admin"); return u;
        }
        await User("admin@handmade.local", "مدیر بازار", "تهران", true);
        var sara = await User("sara@handmade.local", "سارا رضایی", "اصفهان");
        var ali = await User("ali@handmade.local", "علی محمدی", "تهران");
        if (!await db.Categories.AnyAsync()) {
            string[] roots = ["زیورآلات", "خوراکی خانگی", "چوب و دکور", "ربات و الکترونیک", "پوشاک", "هنر و نقاشی"];
            foreach (var name in roots) db.Categories.Add(new() { Name = name }); await db.SaveChangesAsync();
            var parents = await db.Categories.OrderBy(x => x.Id).ToListAsync();
            string[] children = ["گردنبند و دستبند", "کیک و شیرینی", "وسایل چوبی", "ربات آموزشی", "لباس دست‌دوز", "نقاشی و تصویرسازی"];
            for (int i = 0; i < parents.Count; i++) db.Categories.Add(new() { Name = children[i], ParentId = parents[i].Id }); await db.SaveChangesAsync();
        }
        if (!await db.Listings.AnyAsync()) {
            var categories = await db.Categories.Where(x => x.ParentId != null).OrderBy(x => x.Id).ToListAsync();
            string[] titles = ["گردنبند دست‌ساز با سنگ فیروزه", "شیرینی نخودچی خانگی تازه", "چراغ رومیزی چوب گردو", "ربات مسیریاب آموزشی", "مانتو کتان دست‌دوز", "تابلوی آبرنگ کوچه‌های کاشان"];
            long[] prices = [480000, 320000, 1850000, 2400000, 1250000, 960000];
            for (int i = 0; i < titles.Length; i++) db.Listings.Add(new() { Title = titles[i], Description = "ساخته‌شده با دست و با مواد اولیه باکیفیت. هر قطعه با دقت و حوصله ساخته می‌شود. امکان ساخت سفارشی و هماهنگی تحویل از طریق پیام خصوصی وجود دارد.", City = i % 2 == 0 ? "اصفهان" : "تهران", MakerId = i % 2 == 0 ? sara.Id : ali.Id, CategoryId = categories[i].Id, Price = prices[i], Status = ListingStatus.Approved, CreatedAt = DateTime.UtcNow.AddHours(-i * 3) }); await db.SaveChangesAsync();
        }
    }
}
