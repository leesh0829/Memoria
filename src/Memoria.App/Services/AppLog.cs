using System;
using System.IO;

namespace Memoria.App.Services;

/// 데이터 안전 최후 방어선용 최소 로거. 절대 throw 하지 않는다(로깅 실패가 앱을 죽이면 안 됨).
public static class AppLog
{
    private static readonly object Gate = new();

    public static void Error(string source, Exception ex) => Write($"[ERROR] {source}: {ex}");

    public static void Warn(string message) => Write($"[WARN] {message}");

    private static void Write(string line)
    {
        try
        {
            var dir = AppPaths.DataDirectory;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "memoria.log");
            lock (Gate)
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {line}{Environment.NewLine}");
        }
        catch
        {
            // 로깅 실패는 무시한다.
        }
    }
}
