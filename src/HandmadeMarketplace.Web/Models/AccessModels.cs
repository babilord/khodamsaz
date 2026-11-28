using System.ComponentModel.DataAnnotations;

namespace HandmadeMarketplace.Web.Models;

public sealed record AccessPage(string? Phone, string? ReturnUrl, string? State = null,
    int Cooldown = 0, string? Error = null, bool SmsAvailable = true);

public sealed class NewMobileProfile
{
    [Required(ErrorMessage = "نام نمایشی را وارد کنید."), StringLength(80, ErrorMessage = "نام نمایشی حداکثر ۸۰ نویسه است.")]
    public string Name { get; set; } = "";

    [StringLength(80, ErrorMessage = "نام شهر حداکثر ۸۰ نویسه است.")]
    public string? City { get; set; }

    [EmailAddress(ErrorMessage = "ایمیل معتبر وارد کنید.")]
    [StringLength(256, ErrorMessage = "ایمیل بیش از حد طولانی است.")]
    public string? Email { get; set; }

    [StringLength(128, ErrorMessage = "رمز عبور بیش از حد طولانی است.")]
    public string? Password { get; set; }

    [StringLength(128, ErrorMessage = "رمز عبور بیش از حد طولانی است.")]
    public string? Confirmation { get; set; }
}
