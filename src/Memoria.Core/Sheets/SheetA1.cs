namespace Memoria.Core.Sheets;

/// <summary>A1 표기 범위 문자열 생성. 시트명은 항상 작은따옴표로 감싸고 내부 따옴표는 '' 로 이스케이프(공백/특수문자 안전).</summary>
public static class SheetA1
{
    public static string Range(string tabName, string columns)
        => $"'{(tabName ?? string.Empty).Replace("'", "''")}'!{columns}";
}
