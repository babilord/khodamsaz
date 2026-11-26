using Microsoft.AspNetCore.DataProtection;
using System.Globalization;
using HandmadeMarketplace.Application;
using HandmadeMarketplace.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews(o => { o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()); o.ModelBinderProviders.Insert(0, new HandmadeMarketplace.Web.Models.PriceModelBinderProvider()); });
var connectionString = builder.Configuration.GetConnectionString("Marketplace");
if (string.IsNullOrWhiteSpace(connectionString))
{
    var database = new Npgsql.NpgsqlConnectionStringBuilder {
        Host = builder.Configuration["Database:Host"] ?? "localhost",
        Port = builder.Configuration.GetValue("Database:Port", 5432),
        Database = builder.Configuration["Database:Name"] ?? "handmade",
        Username = builder.Configuration["Database:User"] ?? "handmade",
        Password = builder.Configuration["Database:Password"] ?? "",
        Timeout = 5, MaxPoolSize = 30
    };
    connectionString = database.ConnectionString;
}
builder.Services.AddDbContext<MarketplaceDb>(o => o.UseNpgsql(connectionString, n => n.EnableRetryOnFailure(5)));
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
    builder.Services.AddDataProtection().SetApplicationName("HandmadeMarketplace")
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
builder.Services.AddIdentity<AppUser, IdentityRole>(o => { o.User.RequireUniqueEmail = true; o.Password.RequiredLength = 8; o.Lockout.MaxFailedAccessAttempts = 5; }).AddEntityFrameworkStores<MarketplaceDb>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o => { o.LoginPath = "/Account/Login"; o.AccessDeniedPath = "/Account/Denied"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Lax; });
builder.Services.AddScoped<IMarketplace, MarketplaceService>();
builder.Services.AddScoped<IPurchaseDeclarations, PurchaseDeclarationService>();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 32 * 1024 * 1024);

var app = builder.Build();
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("fa-IR");
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("fa-IR");

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

if (app.Configuration.GetValue("HttpsRedirection:Enabled", false)) app.UseHttpsRedirection();
app.Use(async (context, next) => { context.Response.Headers["X-Content-Type-Options"] = "nosniff"; context.Response.Headers["X-Frame-Options"] = "DENY"; await next(); });
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = file =>
    {
        var request = file.Context.Request;
        if (request.Path.StartsWithSegments("/uploads"))
        {
            file.Context.Response.Headers.CacheControl = "public, max-age=86400";
        }
        else if (request.Query.Count == 1 && request.Query.ContainsKey("v"))
        {
            var versions = file.Context.RequestServices.GetRequiredService<Microsoft.AspNetCore.Mvc.ViewFeatures.IFileVersionProvider>();
            var versionedPath = versions.AddFileVersionToPath(request.PathBase, request.Path.Value!);
            if (string.Equals(versionedPath, request.Path.Value + request.QueryString.Value, StringComparison.Ordinal))
                file.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
    }
});
app.UseRouting();
app.Use(async (context, next) =>
{
    var action = context.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>();
    if (action is not null && (action.ControllerName is "Account" or "Admin" or "Messages" or "PurchaseDeclarations"
        || action.ControllerName == "Listings" && action.ActionName is not ("Details" or "Maker")
        || action.ControllerName == "Home" && action.ActionName is "Privacy" or "Error"))
        context.Response.Headers["X-Robots-Tag"] = "noindex";
    await next();
});
app.UseAuthentication();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


if (args.Contains("--seed")) {
    if (!app.Environment.IsDevelopment()) throw new InvalidOperationException("Seed is development-only.");
    using var scope = app.Services.CreateScope(); await DevelopmentSeed.Run(scope.ServiceProvider, DemoPassword()); return;
}
// Complete initialization before accepting HTTP traffic. Migration failures stop startup.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MarketplaceDb>();
    await db.Database.MigrateAsync();
    await DatabaseSeed.Run(scope.ServiceProvider, app.Configuration);
    if (app.Configuration.GetValue("Seed:DemoData", false))
    {
        if (!app.Environment.IsDevelopment()) throw new InvalidOperationException("Demo seed is development-only.");
        await DevelopmentSeed.Run(scope.ServiceProvider, DemoPassword());
    }
}
app.Run();

string DemoPassword() => builder.Configuration["Seed:DemoPassword"] is { Length: > 0 } password
    ? password : throw new InvalidOperationException("Set Seed__DemoPassword for development demo seeding.");

public partial class Program { }
