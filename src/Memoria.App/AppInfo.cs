using System;
using System.Reflection;

namespace Memoria.App;

/// <summary>앱 메타(이름·버전·링크)의 단일 조회 지점. 버전은 어셈블리 버전에서 읽는다.</summary>
public static class AppInfo
{
    public const string Name = "Memoria";
    public const string RepositoryUrl = "https://github.com/leesh0829/Memoria";
    public const string ReleasesUrl = "https://github.com/leesh0829/Memoria/releases";

    /// 표시용 버전 문자열(예: "v0.8.0"). 어셈블리 버전(csproj &lt;Version&gt; / 릴리스 시 태그 주입)에서 유도.
    public static string Version => FormatVersion(Assembly.GetExecutingAssembly().GetName().Version);

    /// Version → "vMAJOR.MINOR.PATCH". null이면 "v0.0.0".
    public static string FormatVersion(Version? v)
        => v is null ? "v0.0.0" : $"v{v.Major}.{v.Minor}.{v.Build}";
}
