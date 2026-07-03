using System;
using System.IO;

namespace Memoria.App;

/// <summary>
/// 모든 데이터 경로의 단일 결정 지점. DB/복구 저널은 %LOCALAPPDATA%\Memoria 하위.
/// </summary>
public static class AppPaths
{
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Memoria");

    public static string DatabaseFile => Path.Combine(DataDirectory, "memoria.db");

    public static string RecoveryDirectory => Path.Combine(DataDirectory, "recovery");

    public static string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(RecoveryDirectory);
        Directory.CreateDirectory(AttachmentsDirectory);
    }
}
