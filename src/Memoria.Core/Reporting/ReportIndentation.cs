namespace Memoria.Core.Reporting;

public static class ReportIndentation
{
    public const string Unit = "  ";
    public const string TaskDetail = "      o";

    public static string NormalizeTabs(string text)
        => text.Replace("\t", Unit, StringComparison.Ordinal);
}
