using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HandmadeMarketplace.Application;
using Microsoft.EntityFrameworkCore;

namespace HandmadeMarketplace.Infrastructure;

public enum OtpPurpose { Register, Login, VerifyPhone, RecoverPassword }
public enum OtpIssue { Accepted, Wait }

public sealed class SmsOptions
{
    public bool Enabled { get; set; }
    public bool Fake { get; set; }
    public string Endpoint { get; set; } = "https://rest.payamak-panel.com/api/SendSMS/BaseServiceNumber";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? ApiKey { get; set; }
    public string? ProviderCredential => string.IsNullOrWhiteSpace(ApiKey) ? Password : ApiKey;
    public int BodyId { get; set; }
    public int TimeoutSeconds { get; set; } = 12;
    public string? HashKey { get; set; }
    public string? TrustedProxy { get; set; }
    public int PhoneHourly { get; set; } = 5;
    public int PhoneDaily { get; set; } = 12;
    public int IpHourly { get; set; } = 20;
    public int IpDaily { get; set; } = 60;

    public bool ValidKey()
    {
        Span<byte> buffer = stackalloc byte[128];
        return HashKey is not null && Convert.TryFromBase64String(HashKey, buffer, out var count) && count >= 32;
    }
}

public interface IOtpSmsSender
{
    Task<bool> Send(string phone, string code, CancellationToken cancellationToken);
}

public sealed class MobileOtpChallenge
{
    public Guid Id { get; set; }
    public OtpPurpose Purpose { get; set; }
    public string PhoneKey { get; set; } = "";
    public string IpKey { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public DateTimeOffset IssuedAt { get; set; }
    public int Attempts { get; set; }
    public bool Closed { get; set; }
    public bool Delivered { get; set; }
}

public sealed class MobileOtp(MarketplaceDb db, SmsOptions options, TimeProvider clock, IOtpSmsSender sender)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(1);
    public const int MaxAttempts = 5;

    public async Task<(OtpIssue Status, Guid Id)> Issue(
        string phone, string ip, OtpPurpose purpose, string? binding, CancellationToken ct)
    {
        var challenges = db.MobileOtpChallenges;
        var id = Guid.NewGuid();
        var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var phoneKey = Digest("phone", phone);
        var ipKey = Digest("ip", ip);
        var hash = binding is null ? "" : CodeHash(id, purpose, phoneKey, binding, code);
        var accepted = await db.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            // The issuance lock serializes shared phone and IP quotas across all app instances.
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(541237022)", token);
            var now = clock.GetUtcNow();
            if (await challenges.AnyAsync(x => x.Id == id, token)) return true;
            var phoneRequests = challenges.Where(x => x.PhoneKey == phoneKey);
            var ipRequests = challenges.Where(x => x.IpKey == ipKey);
            if (await phoneRequests.AnyAsync(x => x.IssuedAt > now - Cooldown, token)
                || await OverLimit(phoneRequests, now, options.PhoneHourly, options.PhoneDaily, token)
                || await OverLimit(ipRequests, now, options.IpHourly, options.IpDaily, token))
                return false;

            await challenges.Where(x => x.IssuedAt < now.AddDays(-1)).ExecuteDeleteAsync(token);
            await phoneRequests.Where(x => x.Purpose == purpose && !x.Closed)
                .ExecuteUpdateAsync(x => x.SetProperty(y => y.Closed, true), token);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "MobileOtpChallenges" ("Id", "Purpose", "PhoneKey", "IpKey", "CodeHash", "IssuedAt", "Attempts", "Closed", "Delivered")
                VALUES ({id}, {(int)purpose}, {phoneKey}, {ipKey}, {hash}, {now}, 0, FALSE, FALSE)
                """, token);
            await transaction.CommitAsync(token);
            return true;
        }, ct);
        if (!accepted) return (OtpIssue.Wait, Guid.Empty);

        // Delivery takes place after commit. Failed or ambiguous delivery never enables a code.
        if (binding is not null && await sender.Send(phone, code, CancellationToken.None))
            await challenges.Where(x => x.Id == id && !x.Closed)
                .ExecuteUpdateAsync(x => x.SetProperty(y => y.Delivered, true), CancellationToken.None);
        return (OtpIssue.Accepted, id);
    }

    public Task<bool> Verify(Guid id, string phone, OtpPurpose purpose, string? binding, string input, CancellationToken ct)
    {
        var code = MobileNumber.Digits(input.Trim());
        if (id == Guid.Empty || code.Length != 6 || !code.All(char.IsAsciiDigit))
            return Task.FromResult(false);
        var phoneKey = Digest("phone", phone);
        var supplied = CodeHash(id, purpose, phoneKey, binding ?? "", code);
        return db.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var challenge = (await db.MobileOtpChallenges
                .FromSqlInterpolated($"""SELECT * FROM "MobileOtpChallenges" WHERE "Id" = {id} FOR UPDATE""")
                .AsNoTracking().ToListAsync(token)).SingleOrDefault();
            if (challenge is null || challenge.Purpose != purpose || challenge.PhoneKey != phoneKey
                || challenge.Closed || challenge.Attempts >= MaxAttempts || challenge.IssuedAt + Lifetime <= clock.GetUtcNow())
                return false;

            var valid = binding is not null && challenge.Delivered && CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(challenge.CodeHash), Encoding.ASCII.GetBytes(supplied));
            await db.MobileOtpChallenges.Where(x => x.Id == id).ExecuteUpdateAsync(x => x
                .SetProperty(y => y.Attempts, challenge.Attempts + 1)
                .SetProperty(y => y.Closed, valid || challenge.Attempts + 1 >= MaxAttempts), token);
            await transaction.CommitAsync(token);
            return valid;
        }, ct);
    }

    private static async Task<bool> OverLimit(IQueryable<MobileOtpChallenge> requests, DateTimeOffset now,
        int hourly, int daily, CancellationToken ct) =>
        await requests.CountAsync(x => x.IssuedAt > now.AddHours(-1), ct) >= hourly
        || await requests.CountAsync(x => x.IssuedAt > now.AddDays(-1), ct) >= daily;

    private string CodeHash(Guid id, OtpPurpose purpose, string phoneKey, string binding, string code) =>
        Digest("code", FormattableString.Invariant($"{id:N}:{(int)purpose}:{phoneKey}:{binding}:{code}"));

    private string Digest(string purpose, string value) => Convert.ToHexString(HMACSHA256.HashData(
        Convert.FromBase64String(options.HashKey!), Encoding.UTF8.GetBytes(purpose + ":" + value)));
}
