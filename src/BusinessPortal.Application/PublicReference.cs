using System.Globalization;

namespace BusinessPortal.Application;

public static class PublicReference
{
    public static string Client(int number) => Format("CLI", number);
    public static string Project(int number) => Format("PRJ", number);
    public static string WorkItem(int number) => Format("WI", number);
    public static string TimeEntry(int number) => Format("TE", number);

    public static bool TryParse(string? value, string prefix, out int number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = value.Trim();
        if (normalized.StartsWith('#')) normalized = normalized[1..];
        if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            normalized = normalized[prefix.Length..].TrimStart('-', ' ', '#');

        return int.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;
    }

    private static string Format(string prefix, int number) => $"{prefix}-{number:0000}";
}
