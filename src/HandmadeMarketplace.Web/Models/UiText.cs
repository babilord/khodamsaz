using System.Globalization;
using HandmadeMarketplace.Domain;
namespace HandmadeMarketplace.Web.Models;
public static class UiText
{
    private static readonly TimeZoneInfo Tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

    public static DateOnly ListingDate(DateTime createdAtUtc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(createdAtUtc, Tehran));

    public static string JalaliDate(DateOnly date)
    {
        var calendar = new PersianCalendar();
        var day = date.ToDateTime(TimeOnly.MinValue);
        var formatted = FormattableString.Invariant(
            $"{calendar.GetYear(day):0000}/{calendar.GetMonth(day):00}/{calendar.GetDayOfMonth(day):00}");
        return string.Concat(formatted.Select(c => c == '/' ? c : (char)('۰' + c - '0')));
    }

    public static string Status(ListingStatus s) => s switch { ListingStatus.Pending => "در انتظار بررسی", ListingStatus.Approved => "منتشرشده", ListingStatus.Rejected => "ردشده", ListingStatus.Sold => "فروخته‌شده", _ => "" };
}
