using System.Globalization;
using System.Text;

namespace HandmadeMarketplace.Application;

public static class PersianPrice
{
    public static string Format(long value) => string.Concat(value.ToString("N0", CultureInfo.InvariantCulture)
        .Select(c => c is >= '0' and <= '9' ? (char)('۰' + c - '0') : c == ',' ? '٬' : c));

    public static bool TryParse(string? input, out long value)
    {
        var normalized = new StringBuilder();
        foreach (var c in (input ?? "").Trim())
        {
            if (c is >= '۰' and <= '۹') normalized.Append((char)('0' + c - '۰'));
            else if (c is >= '٠' and <= '٩') normalized.Append((char)('0' + c - '٠'));
            else if (c is >= '0' and <= '9') normalized.Append(c);
            else if (c != ',' && c != '٬') { value = 0; return false; }
        }
        return long.TryParse(normalized.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
