using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using HandmadeMarketplace.Infrastructure;

namespace HandmadeMarketplace.Web.Services;

public sealed class MeliPayamakSms(HttpClient http, SmsOptions options, ILogger<MeliPayamakSms> logger) : IOtpSmsSender
{
    public async Task<bool> Send(string phone, string code, CancellationToken cancellationToken)
    {
        if (!options.Enabled || options.Fake) return false;
        try
        {
            using var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = options.Username!,
                ["password"] = options.ProviderCredential!,
                ["text"] = code,
                ["to"] = "0" + phone[3..],
                ["bodyId"] = options.BodyId.ToString(CultureInfo.InvariantCulture)
            });
            using var response = await http.PostAsync(options.Endpoint, body, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ProviderResponse>(cancellationToken);
                if (result is { RetStatus: 1 }
                    && long.TryParse(result.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var receipt)
                    && receipt >= 1_000_000_000_000_000)
                    return true;
            }
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException
            or NotSupportedException or IOException)
        {
            // Transport errors can contain details about the request. Log only a generic event below.
        }
        logger.LogWarning("MeliPayamak did not accept an OTP delivery.");
        return false;
    }

    private sealed record ProviderResponse(int RetStatus, string? Value);
}

public sealed class DevelopmentSms(IHostEnvironment environment) : IOtpSmsSender
{
    private readonly ConcurrentDictionary<string, (string Code, DateTimeOffset At)> inbox = new();

    public Task<bool> Send(string phone, string code, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment()) throw new InvalidOperationException("Fake SMS is development-only.");
        foreach (var item in inbox)
            if (item.Value.At < DateTimeOffset.UtcNow - MobileOtp.Lifetime)
                inbox.TryRemove(item);
        inbox[phone] = (code, DateTimeOffset.UtcNow);
        return Task.FromResult(true);
    }

    public string? Take(string phone) => inbox.TryRemove(phone, out var item)
        && item.At + MobileOtp.Lifetime > DateTimeOffset.UtcNow ? item.Code : null;
}
