using System.Net;
using System.Text.RegularExpressions;
using System.Text;

// Runs against the development server and its dedicated, seeded database.
try {
var demoPassword = Environment.GetEnvironmentVariable("Seed__DemoPassword")
    ?? throw new InvalidOperationException("Set Seed__DemoPassword to the password used when seeding the test server.");
var root = args.FirstOrDefault() ?? "http://localhost:5180";
HttpClient Client() => new(new HttpClientHandler { CookieContainer = new(), AllowAutoRedirect = false }) { BaseAddress = new(root) };
using var guest = Client(); using var seller = Client(); using var buyer = Client(); using var admin = Client();
int checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); checks++; }
async Task<string> Page(HttpClient client, string path) { var r = await client.GetAsync(path); Check(r.StatusCode == HttpStatusCode.OK, "GET " + path); return WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()); }
string Token(string html) => Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
async Task<HttpResponseMessage> Post(HttpClient client, string path, string page, params (string Key, string Value)[] values) {
    var fields = values.Select(x => new KeyValuePair<string,string>(x.Key, x.Value)).Append(new("__RequestVerificationToken", Token(page)));
    return await client.PostAsync(path, new FormUrlEncodedContent(fields));
}
async Task Login(HttpClient client, string email) { var p = await Page(client, "/Account/Login"); var r = await Post(client, "/Account/Login", p, ("Email", email), ("Password", demoPassword)); Check(r.StatusCode == HttpStatusCode.Redirect, "login " + email); }
var home = await Page(guest, "/"); Check(home.Contains("dir=\"rtl\"") && home.Contains("گردنبند"), "RTL Persian sample listings");
Check((await guest.GetAsync("/Listings/Mine")).StatusCode == HttpStatusCode.Redirect, "anonymous management blocked");
Check((await guest.PostAsync("/Account/Register", new FormUrlEncodedContent([]))).StatusCode == HttpStatusCode.BadRequest, "CSRF enforced");
var filtered = await Page(guest, "/?Q=" + Uri.EscapeDataString("گردنبند") + "&Min=400000&Max=500000&City=" + Uri.EscapeDataString("اصفهان"));
Check(filtered.Contains("گردنبند دست‌ساز") && !filtered.Contains("ربات مسیریاب آموزشی"), "search city and price filters");
await Login(seller, "sara@handmade.local"); await Login(buyer, "ali@handmade.local"); await Login(admin, "admin@handmade.local");
var forbidden = await buyer.GetAsync("/Admin"); Check(forbidden.StatusCode == HttpStatusCode.Redirect && forbidden.Headers.Location!.ToString().Contains("Denied"), "admin role enforced");
var profile = await Page(seller, "/Account/Profile");
Check((await Post(seller, "/Account/Profile", profile, ("Name", "سارا رضایی"), ("City", "اصفهان"), ("Bio", "سازنده زیورآلات و هنرهای دستی؛ سفارش اختصاصی می‌پذیرم."))).StatusCode == HttpStatusCode.Redirect, "profile update");
var edit = await Page(seller, "/Listings/Edit");
var category = Regex.Match(edit, "<option value=\"([1-9][0-9]*)\"").Groups[1].Value;
const string descriptionLengthMessage = "لطفاً توضیحات محصول را بین ۱۰ تا ۴۰۰۰ کاراکتر وارد کنید.";
async Task CheckListingError(string field, string value, string message)
{
    var fields = new Dictionary<string, string> {
        ["Title"] = "محصول آزمایشی", ["Description"] = new string('ا', 20),
        ["City"] = "تهران", ["CategoryId"] = category, ["Price"] = "۰"
    };
    fields[field] = value;
    using var response = await Post(seller, "/Listings/Edit", edit, fields.Select(x => (x.Key, x.Value)).ToArray());
    var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    Check(response.StatusCode == HttpStatusCode.OK && html.Contains($"<li>{message}</li>"), $"Persian listing validation for {field}");
}
await CheckListingError("Description", new string('ا', 9), descriptionLengthMessage);
await CheckListingError("Description", new string('ا', 4001), descriptionLengthMessage);
await CheckListingError("Description", "", "لطفاً توضیحات محصول را وارد کنید.");
await CheckListingError("Title", new string('ا', 121), "عنوان محصول نباید بیشتر از ۱۲۰ کاراکتر باشد.");
await CheckListingError("City", new string('ا', 81), "نام شهر نباید بیشتر از ۸۰ کاراکتر باشد.");
await CheckListingError("CategoryId", "", "لطفاً یک دسته‌بندی معتبر انتخاب کنید.");
await CheckListingError("CategoryId", "invalid", "لطفاً یک دسته‌بندی معتبر انتخاب کنید.");
var invalidInput = await Post(seller, "/Listings/Edit", edit, ("Title", "نامعتبر"), ("Description", "شرح کافی برای بررسی اعتبارسنجی قیمت"), ("City", "تهران"), ("Price", "-1"), ("CategoryId", category));
Check(invalidInput.StatusCode == HttpStatusCode.OK && (await invalidInput.Content.ReadAsStringAsync()).Contains("validation-summary-errors"), "negative price rejected");
using (var invalidFile = new MultipartFormDataContent()) {
    foreach (var field in new Dictionary<string,string> { ["__RequestVerificationToken"] = Token(edit), ["Title"] = "فایل نامعتبر", ["Description"] = "توضیح کافی برای آزمون فایل نامعتبر", ["City"] = "تهران", ["Price"] = "1000", ["CategoryId"] = category }) invalidFile.Add(new StringContent(field.Value), field.Key);
    invalidFile.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("<script>alert(1)</script>")), "images", "fake.png");
    var rejected = await seller.PostAsync("/Listings/Edit", invalidFile);
    Check(rejected.StatusCode == HttpStatusCode.OK && WebUtility.HtmlDecode(await rejected.Content.ReadAsStringAsync())
        .Contains("<li>فقط تصاویر JPEG و PNG مجازند.</li>"), "forged image rejected with Persian message");
}
var title = "آزمایش خودکار " + Guid.NewGuid().ToString("N")[..8];
using var multipart = new MultipartFormDataContent();
void Field(string key, string value) => multipart.Add(new StringContent(value), key);
Field("__RequestVerificationToken", Token(edit)); Field("Title", title); Field("Description", new string('ا', 10)); Field("City", "شیراز"); Field("Price", "750000"); Field("CategoryId", category); Field("Id", "0");
byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
multipart.Add(new ByteArrayContent(png), "images", "first.png"); multipart.Add(new ByteArrayContent(png), "images", "second.png");
Check((await seller.PostAsync("/Listings/Edit", multipart)).StatusCode == HttpStatusCode.Redirect, "create listing with two images");
var mine = await Page(seller, "/Listings/Mine");
var id = Regex.Match(mine, "href=\"/Listings/Details/([0-9]+)\"[^>]*>" + Regex.Escape(title)).Groups[1].Value;
Check(id.Length > 0, "created listing exists");
Check((await guest.GetAsync("/Listings/Details/" + id)).StatusCode == HttpStatusCode.NotFound, "pending listing hidden");
Check(!(await Page(guest, "/sitemap.xml")).Contains($"/Listings/Details/{id}</loc>"), "pending listing excluded from sitemap");
Check((await buyer.GetAsync("/Listings/Edit/" + id)).StatusCode == HttpStatusCode.NotFound, "other seller edit blocked");
var buyerMine = await Page(buyer, "/Listings/Mine");
Check((await Post(buyer, "/Listings/Delete", buyerMine, ("id", id))).StatusCode == HttpStatusCode.NotFound, "other seller delete blocked");
var detail = await Page(seller, "/Listings/Details/" + id); Check(Regex.Matches(detail, "class=\"thumb\"").Count == 2, "two images persisted");
var upload = Regex.Match(detail, "src=\"(/uploads/[^\"]+)\"").Groups[1].Value; Check((await guest.GetAsync(upload)).IsSuccessStatusCode, "uploaded image served");
var dashboard = await Page(admin, "/Admin");
Check((await Post(admin, "/Admin/Moderate", dashboard, ("id", id), ("approve", "true"))).StatusCode == HttpStatusCode.Redirect, "admin approves listing");
Check((await Page(guest, "/sitemap.xml")).Contains($"/Listings/Details/{id}</loc>"), "approved listing enters sitemap");
detail = await Page(guest, "/Listings/Details/" + id);
var makerPath = Regex.Match(detail, "href=\"(/Listings/Maker/[^\"]+)\"").Groups[1].Value;
Check((await Page(guest, makerPath)).Contains(title), "public maker profile");
var makerId = makerPath.Split('/').Last();
var favoritesPage = await Page(buyer, "/Listings/Favorites");
Check((await Post(buyer, "/Listings/Favorite", favoritesPage, ("id", id))).StatusCode == HttpStatusCode.Redirect, "favorite toggle");
Check((await Page(buyer, "/Listings/Favorites")).Contains(title), "favorite persisted");
favoritesPage = await Page(buyer, "/Listings/Favorites");
await Post(buyer, "/Listings/Favorite", favoritesPage, ("id", id));
Check(!(await Page(buyer, "/Listings/Favorites")).Contains(title), "favorite removal persisted");
var threadUrl = "/Messages/Thread/" + id + "?other=" + makerId;
var thread = await Page(buyer, threadUrl);
Check((await Post(buyer, "/Messages/Send", thread, ("id", id), ("other", makerId), ("body", "سلام، امکان سفارش دارید؟"))).StatusCode == HttpStatusCode.Redirect, "buyer sends message");
Check((await Page(buyer, threadUrl)).Contains("امکان سفارش"), "message persisted");
var sellerInbox = await Page(seller, "/Messages");
var replyUrl = Regex.Match(sellerInbox, "href=\"(/Messages/Thread/" + id + "[^\"]*)\"").Groups[1].Value;
var replyPage = await Page(seller, replyUrl);
var buyerId = Regex.Match(replyPage, "name=\"other\" value=\"([^\"]+)\"").Groups[1].Value;
Check((await Post(seller, "/Messages/Send", replyPage, ("id", id), ("other", buyerId), ("body", "بله، با خوشحالی می‌سازم."))).StatusCode == HttpStatusCode.Redirect, "seller replies");
Check((await Page(buyer, threadUrl)).Contains("با خوشحالی"), "reply visible to buyer");
Check((await admin.GetAsync(replyUrl)).StatusCode == HttpStatusCode.NotFound, "private conversation hidden from third party");
// Edits must return an approved listing to moderation.
edit = await Page(seller, "/Listings/Edit/" + id);
Check((await Post(seller, "/Listings/Edit", edit, ("Id", id), ("Title", title), ("Description", new string('ا', 4000)), ("City", "شیراز"), ("Price", "800000"), ("CategoryId", category))).StatusCode == HttpStatusCode.Redirect, "edit listing accepts 4000-character description");
Check((await guest.GetAsync("/Listings/Details/" + id)).StatusCode == HttpStatusCode.NotFound, "edited listing requires reapproval");
dashboard = await Page(admin, "/Admin");
await Post(admin, "/Admin/Moderate", dashboard, ("id", id), ("approve", "false"));
Check((await guest.GetAsync("/Listings/Details/" + id)).StatusCode == HttpStatusCode.NotFound, "rejected listing hidden");
await Post(admin, "/Admin/Moderate", dashboard, ("id", id), ("approve", "true"));
Check((await Page(guest, "/?CategoryId=" + category + "&Q=" + Uri.EscapeDataString(title))).Contains(title), "category filter includes approved listing");
mine = await Page(seller, "/Listings/Mine");
Check((await Post(seller, "/Listings/Sold", mine, ("id", id))).StatusCode == HttpStatusCode.Redirect, "mark sold");
Check((await guest.GetAsync("/Listings/Details/" + id)).StatusCode == HttpStatusCode.NotFound, "sold listing hidden from public");
mine = await Page(seller, "/Listings/Mine"); Check((await Post(seller, "/Listings/Delete", mine, ("id", id))).StatusCode == HttpStatusCode.Redirect, "delete owned listing and related messages");
Check(!(await Page(seller, "/Listings/Mine")).Contains(title), "deleted listing removed");
Check((await guest.GetAsync("/Listings/Details/" + id)).StatusCode == HttpStatusCode.NotFound, "deleted product returns 404");
Check(!(await Page(guest, "/sitemap.xml")).Contains($"/Listings/Details/{id}</loc>"), "deleted listing leaves sitemap");
Check((await guest.GetAsync(upload)).StatusCode == HttpStatusCode.NotFound, "deleted listing image removed");
var registration = await Page(guest, "/Account/Register"); var email = "smoke-" + Guid.NewGuid().ToString("N") + "@example.test";
Check((await Post(guest, "/Account/Register", registration, ("Name", "کاربر آزمایشی"), ("City", "رشت"), ("Email", email), ("Password", demoPassword))).StatusCode == HttpStatusCode.Redirect, "registration and automatic sign-in");
var registeredProfile = await Page(guest, "/Account/Profile");
var logoutGet = (await guest.GetAsync("/Account/Logout")).StatusCode;
Check(logoutGet is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound, "logout requires POST");
Check((await guest.PostAsync("/Account/Logout", new FormUrlEncodedContent([]))).StatusCode == HttpStatusCode.BadRequest, "logout requires antiforgery token");
Check((await Post(guest, "/Account/Logout", registeredProfile)).StatusCode == HttpStatusCode.Redirect, "logout succeeds with antiforgery token");
Check((await guest.GetAsync("/Account/Profile")).StatusCode == HttpStatusCode.Redirect, "logout removes authenticated access");
Console.WriteLine($"All {checks} checks passed.");
} catch (Exception e) { Console.Error.WriteLine(e.Message); Environment.ExitCode = 1; }
