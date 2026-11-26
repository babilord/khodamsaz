namespace HandmadeMarketplace.Application;

public static class MobileNumber
{
    public static string? Normalize(string? input)
    {
        if (input is null || input.Length > 40) return null;
        var digits = Digits(input.Trim().Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", ""));
        var national = digits switch
        {
            { Length: 11 } when digits.StartsWith("09", StringComparison.Ordinal) => digits[1..],
            { Length: 13 } when digits.StartsWith("+989", StringComparison.Ordinal) => digits[3..],
            { Length: 12 } when digits.StartsWith("989", StringComparison.Ordinal) => digits[2..],
            _ => null
        };
        return national is not null && national.All(char.IsAsciiDigit) ? "+98" + national : null;
    }

    public static string Digits(string value) => string.Concat(value.Select(c => c switch
    {
        >= '۰' and <= '۹' => (char)('0' + c - '۰'),
        >= '٠' and <= '٩' => (char)('0' + c - '٠'),
        _ => c
    }));
}
