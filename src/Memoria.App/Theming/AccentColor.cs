// src/Memoria.App/Theming/AccentColor.cs
using System.Text.RegularExpressions;

namespace Memoria.App.Theming;

public static partial class AccentColor
{
    public const string Default = "#0078D4";

    [GeneratedRegex("^#?[0-9A-Fa-f]{6}$")]
    private static partial Regex HexPattern();

    public static bool IsValid(string? hex)
        => !string.IsNullOrWhiteSpace(hex) && HexPattern().IsMatch(hex.Trim());

    public static string Normalize(string? hex)
    {
        if (!IsValid(hex))
            return Default;

        var trimmed = hex!.Trim().TrimStart('#');
        return "#" + trimmed.ToUpperInvariant();
    }
}
