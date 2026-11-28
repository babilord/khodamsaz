using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using HandmadeMarketplace.Application;
using HandmadeMarketplace.Infrastructure;
using HandmadeMarketplace.Web.Controllers;
using HandmadeMarketplace.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

internal static class MobileOtpChecks
{
    public static async Task Run(string connection, Action<bool, string> check)
    {
        foreach (var value in new[] { "09123456789", "+989123456789", "989123456789", "۰۹۱۲۳۴۵۶۷۸۹", "٠٩١٢٣٤٥٦٧٨٩" })
            check(MobileNumber.Normalize(value) == "+989123456789", "Iranian phone normalization");
        check(MobileNumber.Normalize("+982112345678") is null && MobileNumber.Normalize("0912345678a") is null,
            "reject landlines and invalid phone digits");

        var settings = new SmsOptions { Enabled = true, Fake = true, HashKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            PhoneHourly = 2, PhoneDaily = 3, IpHourly = 2, IpDaily = 3 };
        var clock = new TestClock();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(MobileAccountController).Assembly.GetName().Name,
            EnvironmentName = "Development"
        });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton<DevelopmentSms>();
        builder.Services.AddSingleton<IOtpSmsSender>(s => s.GetRequiredService<DevelopmentSms>());
        builder.Services.AddScoped<MobileOtp>();
        builder.Services.AddDbContext<MarketplaceDb>(o => o.UseNpgsql(connection, n => n.EnableRetryOnFailure()));
        builder.Services.AddIdentity<AppUser, IdentityRole>().AddEntityFrameworkStores<MarketplaceDb>().AddDefaultTokenProviders().AddUserValidator<OptionalEmailValidator>();
        builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
            .AddApplicationPart(typeof(MobileAccountController).Assembly);
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllerRoute("default", "{controller=Account}/{action=Login}/{id?}");
        var fake = app.Services.GetRequiredService<DevelopmentSms>();
        const string phone = "+989123456789";
        const string ip = "test-ip";
        await using var scope = app.Services.CreateAsyncScope();
        var otp = scope.ServiceProvider.GetRequiredService<MobileOtp>();
        var db = scope.ServiceProvider.GetRequiredService<MarketplaceDb>();
        var issued = await otp.Issue(phone, ip, OtpPurpose.Login, "account", default);
        var code = fake.Take(phone)!;
        check(code.Length == 6 && code.All(char.IsAsciiDigit), "fake delivery uses six-digit cryptographic OTP format");
        check(await db.MobileOtpChallenges.AllAsync(x => x.CodeHash != code && x.PhoneKey != phone), "OTP and throttle identifiers are keyed hashes");
        check((await otp.Issue(phone, ip, OtpPurpose.RecoverPassword, "account", default)).Status == OtpIssue.Wait,
            "one-minute cooldown applies across purposes");
        check(!await otp.Verify(issued.Id, phone, OtpPurpose.RecoverPassword, "account", code, default), "purpose separation");
        var wrong = code == "111111" ? "222222" : "111111";
        for (var attempt = 0; attempt < 5; attempt++)
            check(!await otp.Verify(issued.Id, phone, OtpPurpose.Login, "account", wrong, default), "incorrect OTP rejected");
        check(!await otp.Verify(issued.Id, phone, OtpPurpose.Login, "account", code, default), "five attempts exhaust the challenge");
        clock.Advance(TimeSpan.FromMinutes(1));
        issued = await otp.Issue(phone, ip, OtpPurpose.Login, "account", default);
        code = fake.Take(phone)!;
        clock.Advance(TimeSpan.FromMinutes(2));
        check(!await otp.Verify(issued.Id, phone, OtpPurpose.Login, "account", code, default), "two-minute expiration");
        check((await otp.Issue(phone, "other-ip", OtpPurpose.Login, "account", default)).Status == OtpIssue.Wait, "phone hourly quota");
        check((await otp.Issue("+989223456789", ip, OtpPurpose.Login, "account", default)).Status == OtpIssue.Wait, "IP hourly quota");
        clock.Advance(TimeSpan.FromHours(1));
        var previous = await otp.Issue(phone, ip, OtpPurpose.Login, "account", default);
        code = fake.Take(phone)!;
        clock.Advance(TimeSpan.FromMinutes(1));
        check((await otp.Issue(phone, ip, OtpPurpose.Login, "account", default)).Status == OtpIssue.Wait, "phone daily quota");
        check((await otp.Issue("+989223456789", ip, OtpPurpose.Login, "account", default)).Status == OtpIssue.Wait, "IP daily quota");

        code = fake.Take(phone) ?? code;
        async Task<bool> TryConsume()
        {
            await using var attempt = app.Services.CreateAsyncScope();
            return await attempt.ServiceProvider.GetRequiredService<MobileOtp>()
                .Verify(previous.Id, phone, OtpPurpose.Login, "account", code, default);
        }
        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => TryConsume()));
        check(attempts.Count(x => x) == 1, "one concurrent verification succeeds");
        check(!await TryConsume(), "OTP replay rejected");

        const string resendPhone = "+989523456789";
        var old = await otp.Issue(resendPhone, "resend-ip", OtpPurpose.Login, "account", default);
        var oldCode = fake.Take(resendPhone)!;
        clock.Advance(TimeSpan.FromMinutes(1));
        var renewed = await otp.Issue(resendPhone, "resend-ip", OtpPurpose.Login, "account", default);
        check(!await otp.Verify(old.Id, resendPhone, OtpPurpose.Login, "account", oldCode, default)
            && await otp.Verify(renewed.Id, resendPhone, OtpPurpose.Login, "account", fake.Take(resendPhone)!, default),
            "resend invalidates the previous challenge");

        var failure = new MobileOtp(db, settings, clock, new RejectingSender());
        var unavailable = await failure.Issue("+989323456789", "failure-ip", OtpPurpose.Register, "new-account", default);
        check(unavailable.Status == OtpIssue.Accepted && !await db.MobileOtpChallenges
            .Where(x => x.Id == unavailable.Id).Select(x => x.Delivered).SingleAsync(), "provider failure leaves a generic response and unusable challenge");
        settings.Fake = false;
        settings.Username = "fixture";
        settings.Password = Guid.NewGuid().ToString("N");
        settings.BodyId = 42;
        using var http = new HttpClient(new ProviderHandler());
        var sender = new MeliPayamakSms(http, settings, NullLogger<MeliPayamakSms>.Instance);
        check(await sender.Send(phone, "123456", default), "provider's accepted-response contract");
        settings.Fake = true;

        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        var baseUrl = app.Urls.Single();
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(baseUrl) };
        const string journeyPhone = "+989423456789";
        var mobilePassword = "Aa1!" + Guid.NewGuid().ToString("N");
        var newPassword = "Aa1!" + Guid.NewGuid().ToString("N");
        var email = "otp-fixture@example.test";
        settings.PhoneDaily = settings.PhoneHourly = 10;
        settings.IpDaily = settings.IpHourly = 100;

        async Task<HttpResponseMessage> Post(string path, string previousPage, params (string, string)[] fields)
        {
            using var form = new FormUrlEncodedContent(fields.Append(("__RequestVerificationToken", Field(previousPage, "__RequestVerificationToken")))
                .Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2)));
            return await client.PostAsync(path, form);
        }

        async Task<(string Page, string Code)> Send(string purpose)
        {
            var path = "/account/mobile/" + purpose;
            var entry = purpose is "Register" or "Login" ? "/account/access" : path;
            using var response = await Post(path + "/send", await client.GetStringAsync(entry), ("Phone", journeyPhone));
            var markup = await response.Content.ReadAsStringAsync();
            return (markup, fake.Take(journeyPhone) ?? "");
        }

        var registered = await Send("Register");
        using var register = await Post("/account/mobile/Register/verify", registered.Page, ("Phone", journeyPhone),
            ("Challenge", Field(registered.Page, "Challenge", true)), ("Code", registered.Code),
            ("Email", email), ("Password", mobilePassword), ("Name", "OTP Test"), ("City", "Qazvin"));
        check(register.StatusCode == HttpStatusCode.Redirect && await db.Users.AnyAsync(x =>
            x.Email == email && x.PhoneNumberConfirmed && x.VerifiedMobile == journeyPhone), "mobile registration creates a verified account");
        var profile = await client.GetStringAsync("/Account/Profile");
        using var logout = await Post("/Account/Logout", profile);
        check(logout.StatusCode == HttpStatusCode.Redirect, "registered user can sign out");

        clock.Advance(TimeSpan.FromMinutes(1));
        var login = await Send("Login");
        using var loginResult = await Post("/account/mobile/Login/verify", login.Page, ("Phone", journeyPhone),
            ("Challenge", Field(login.Page, "Challenge", true)), ("Code", login.Code));
        check(loginResult.StatusCode == HttpStatusCode.Redirect && (await client.GetAsync("/Account/Profile")).IsSuccessStatusCode, "OTP login issues Identity application cookie");

        clock.Advance(TimeSpan.FromMinutes(1));
        var recover = await Send("RecoverPassword");
        using var reset = await Post("/account/mobile/RecoverPassword/verify", recover.Page, ("Phone", journeyPhone),
            ("Challenge", Field(recover.Page, "Challenge", true)), ("Code", recover.Code), ("Password", newPassword));
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var account = await users.FindByEmailAsync(email);
        check(reset.StatusCode == HttpStatusCode.Redirect && account is not null && await users.CheckPasswordAsync(account, newPassword), "password recovery uses Identity reset");
        clock.Advance(TimeSpan.FromMinutes(1));
        var duplicate = await Send("Register");
        check(duplicate.Code.Length == 0 && duplicate.Page.Contains("otp-code"), "duplicate verified number receives the same screen without a usable code");
        clock.Advance(TimeSpan.FromMinutes(1));
        check((await users.SetTwoFactorEnabledAsync(account!, true)).Succeeded, "configure fixture MFA");
        check((await Send("Login")).Code.Length == 0, "SMS cannot replace MFA");
        check((await users.SetTwoFactorEnabledAsync(account!, false)).Succeeded, "clear fixture MFA");
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        check((await roles.CreateAsync(new IdentityRole("Admin"))).Succeeded
            && (await users.AddToRoleAsync(account!, "Admin")).Succeeded, "configure fixture administrator");
        clock.Advance(TimeSpan.FromMinutes(1));
        check((await Send("RecoverPassword")).Code.Length == 0, "SMS cannot recover administrator credentials");
        check((await users.RemoveFromRoleAsync(account!, "Admin")).Succeeded
            && (await users.SetLockoutEndDateAsync(account!, DateTimeOffset.UtcNow.AddMinutes(5))).Succeeded,
            "lock fixture account");
        clock.Advance(TimeSpan.FromMinutes(1));
        check((await Send("Login")).Code.Length == 0, "OTP cannot sign in a locked account");
        using var noToken = await client.PostAsync("/account/mobile/Login/send", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["Phone"] = journeyPhone }));
        check(noToken.StatusCode == HttpStatusCode.BadRequest, "mobile writes require anti-forgery token");
        const string accessPhone = "+989533456789";
        const string optionalPhone = "+989633456789";
        using var directProfile = await client.GetAsync("/account/access/profile");
        check(directProfile.StatusCode == HttpStatusCode.Redirect, "profile completion requires a verified continuation");

        async Task<(string Page, string State, string Code)> SendAccess(string number, string returnUrl)
        {
            using var response = await Post("/account/access/send", await client.GetStringAsync("/account/access"),
                ("phone", number), ("returnUrl", returnUrl));
            var markup = await response.Content.ReadAsStringAsync();
            return (markup, Field(markup, "state"), fake.Take(number) ?? "");
        }

        var access = await SendAccess(accessPhone, "/Account/Profile");
        check(access.State.Length > 0 && access.Code.Length == 6 && !access.Page.Contains("name=\"Email\""),
            "unified OTP screen defers registration fields");
        using var wrongAccess = await Post("/account/access/verify", access.Page, ("state", access.State),
            ("code", access.Code == "123456" ? "234567" : "123456"));
        check(wrongAccess.StatusCode == HttpStatusCode.OK, "unified access rejects incorrect OTP");
        using var verifiedAccess = await Post("/account/access/verify", access.Page, ("state", access.State), ("code", access.Code));
        check(verifiedAccess.Headers.Location?.OriginalString == "/account/access/profile", "new verified number enters profile completion");
        var mobileProfile = await client.GetStringAsync("/account/access/profile");
        var completions = await Task.WhenAll(
            Post("/account/access/profile", mobileProfile, ("Name", "Mobile Fixture")),
            Post("/account/access/profile", mobileProfile, ("Name", "Mobile Fixture")));
        foreach (var completion in completions) completion.Dispose();
        check(await db.Users.CountAsync(x => x.VerifiedMobile == accessPhone && x.Email == null && x.PasswordHash == null) == 1,
            "concurrent passwordless completion creates exactly one account");
        using var accessLogout = await Post("/Account/Logout", await client.GetStringAsync("/Account/Profile"));
        clock.Advance(TimeSpan.FromMinutes(1));
        access = await SendAccess(accessPhone, "/Account/Profile");
        using var existing = await Post("/account/access/verify", access.Page, ("state", access.State), ("code", access.Code));
        check(existing.Headers.Location?.OriginalString == "/Account/Profile", "existing mobile user returns to its local destination");
        using var existingLogout = await Post("/Account/Logout", await client.GetStringAsync("/Account/Profile"));

        var optional = await SendAccess(optionalPhone, "//example.test");
        using var optionalVerify = await Post("/account/access/verify", optional.Page, ("state", optional.State), ("code", optional.Code));
        var optionalEmail = "optional-otp@example.test";
        using var optionalResult = await Post("/account/access/profile", await client.GetStringAsync("/account/access/profile"),
            ("Name", "Optional Fixture"), ("Email", optionalEmail), ("Password", mobilePassword), ("Confirmation", mobilePassword));
        check(optionalResult.Headers.Location?.OriginalString is ("/" or "/Home/Index") && await db.Users.AnyAsync(x =>
            x.VerifiedMobile == optionalPhone && x.Email == optionalEmail && x.PasswordHash != null && !x.EmailConfirmed),
            "optional email and password work and external return URLs are discarded");
        using var optionalLogout = await Post("/Account/Logout", await client.GetStringAsync("/Account/Profile"));
        using var passwordLogin = await Post("/Account/Login", await client.GetStringAsync("/Account/Login"),
            ("Email", optionalEmail), ("Password", mobilePassword));
        check(passwordLogin.StatusCode == HttpStatusCode.Redirect, "optional password works with existing Identity email sign-in");
        using var passwordLogout = await Post("/Account/Logout", await client.GetStringAsync("/Account/Profile"));

        var restricted = await SendAccess(journeyPhone, "/Admin");
        check(restricted.Code.Length == 6, "unified issuance does not disclose locked-account eligibility");
        clock.Advance(TimeSpan.FromMinutes(3));
        using var expired = await Post("/account/access/verify", restricted.Page, ("state", restricted.State), ("code", restricted.Code));
        check(expired.StatusCode == HttpStatusCode.OK, "expired unified OTP is rejected");
        restricted = await SendAccess(journeyPhone, "/Admin");
        using var restrictedResult = await Post("/account/access/verify", restricted.Page, ("state", restricted.State), ("code", restricted.Code));
        check(restrictedResult.StatusCode == HttpStatusCode.OK && (await client.GetAsync("/Account/Profile")).StatusCode == HttpStatusCode.Redirect,
            "a verified OTP cannot sign in a locked existing account");


        const string linkPhone = "+989733456789";
        const string linkEmail = "link-otp@example.test";
        using var emailRegistration = await Post("/Account/Register", await client.GetStringAsync("/Account/Register"),
            ("Name", "Link Fixture"), ("City", "Qazvin"), ("Email", linkEmail), ("Password", mobilePassword));
        check(emailRegistration.StatusCode == HttpStatusCode.Redirect, "existing email registration flow works");
        const string linkPath = "/account/mobile/VerifyPhone";
        using var linkSend = await Post(linkPath + "/send", await client.GetStringAsync(linkPath), ("Phone", linkPhone));
        var linkPage = await linkSend.Content.ReadAsStringAsync();
        using var linkVerify = await Post(linkPath + "/verify", linkPage, ("Phone", linkPhone),
            ("Challenge", Field(linkPage, "Challenge", true)), ("Code", fake.Take(linkPhone) ?? ""), ("Password", mobilePassword));
        check(linkVerify.StatusCode == HttpStatusCode.Redirect && await db.Users.AnyAsync(x =>
            x.Email == linkEmail && x.VerifiedMobile == linkPhone && x.PhoneNumberConfirmed), "email-only account can verify and link a phone");
        using var linkLogout = await Post("/Account/Logout", await client.GetStringAsync("/Account/Profile"));


        settings.Enabled = false;
        check((await client.GetAsync("/account/mobile/Login")).StatusCode == HttpStatusCode.NotFound, "disabled SMS route is hidden");
        check(!(await client.GetStringAsync("/Account/Login")).Contains("/account/mobile/", StringComparison.OrdinalIgnoreCase),
            "disabled SMS is absent from email login");
    }

    private static string Field(string html, string name, bool last = false)
    {
        var values = Regex.Matches(html, "name=\"" + name + "\"[^>]*value=\"([^\"]+)\"");
        return values.Count == 0 ? "" : WebUtility.HtmlDecode(values[last ? values.Count - 1 : 0].Groups[1].Value);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan delta) => now += delta;
    }

    private sealed class RejectingSender : IOtpSmsSender
    {
        public Task<bool> Send(string phone, string code, CancellationToken ct) => Task.FromResult(false);
    }

    private sealed class ProviderHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            if (!body.Split('&').Contains("text=123456") || !body.Contains("to=09123456789")
                || !request.RequestUri!.AbsolutePath.EndsWith("/BaseServiceNumber"))
                throw new InvalidOperationException("Unexpected template REST request.");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"RetStatus":1,"Value":"5084123456789012345","StrRetStatus":"Ok"}""", Encoding.UTF8, "application/json")
            };
        }
    }
}
