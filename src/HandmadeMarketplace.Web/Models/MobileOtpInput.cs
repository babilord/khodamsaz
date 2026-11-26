using System.ComponentModel.DataAnnotations;
using HandmadeMarketplace.Infrastructure;

namespace HandmadeMarketplace.Web.Models;

public sealed class MobileOtpInput
{
    public string? Phone { get; set; }
    public Guid Challenge { get; set; }
    public string? Code { get; set; }
    public string? ReturnUrl { get; set; }
    public string? Name { get; set; }
    public string? City { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }

    public bool ValidDetails(OtpPurpose purpose) =>
        (purpose == OtpPurpose.Login || Password is { Length: >= 8 and <= 128 })
        && (purpose != OtpPurpose.Register ||
            Name?.Trim() is { Length: > 0 and <= 80 } && City?.Trim() is { Length: > 0 and <= 80 }
            && Email?.Trim() is { Length: > 0 and <= 256 } address && new EmailAddressAttribute().IsValid(address));
}

public sealed record MobileOtpPage(OtpPurpose Purpose, MobileOtpInput Input, string? Message, int Cooldown);
