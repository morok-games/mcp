using System.Text.RegularExpressions;

namespace Platform.Api.Catalog;

/// <summary>ISO 8601 durations as stored in the catalog: "P6M", "PT45M", "P1Y2M".</summary>
public static partial class IsoDuration
{
    public static bool IsValid(string? value) => value is not null && Pattern().IsMatch(value);

    [GeneratedRegex(@"^P(?!$)(\d+Y)?(\d+M)?(\d+W)?(\d+D)?(T(?=\d)(\d+H)?(\d+M)?(\d+S)?)?$")]
    private static partial Regex Pattern();
}
