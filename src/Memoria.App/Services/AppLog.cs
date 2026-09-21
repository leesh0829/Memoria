using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Memoria.App.Services;

/// 데이터 안전 최후 방어선용 최소 로거. 절대 throw 하지 않는다(로깅 실패가 앱을 죽이면 안 됨).
public static class AppLog
{
    private static readonly object Gate = new();
    private const long MaxLogBytes = 2 * 1024 * 1024;

    public static void Error(string source, Exception ex) => Write($"[ERROR] {source}: {ex}");

    public static void Warn(string message) => Write($"[WARN] {message}");

    public static IDisposable Measure(string operation, int warnAfterMs = 250)
        => new PerformanceScope(operation, warnAfterMs);

    private sealed class PerformanceScope(string operation, int warnAfterMs) : IDisposable
    {
        private readonly long _started = Stopwatch.GetTimestamp();
        private bool _finished;

        public void Dispose()
        {
            if (_finished) return;
            _finished = true;
            var elapsed = Stopwatch.GetElapsedTime(_started);
            if (elapsed.TotalMilliseconds >= warnAfterMs)
            {
                var line = $"[PERF] operation={operation} elapsed_ms={elapsed.TotalMilliseconds:F0}";
                ThreadPool.QueueUserWorkItem(_ => Write(line));
            }
        }
    }

    private static void Write(string line)
    {
        try
        {
            // 테스트의 의도적 예외가 실제 사용자 로그에 섞이지 않게 분리한다.
            var processName = Process.GetCurrentProcess().ProcessName;
            var isTestHost = processName.Contains("testhost", StringComparison.OrdinalIgnoreCase)
                || processName.Contains("vstest", StringComparison.OrdinalIgnoreCase);
            var dir = isTestHost ? Path.Combine(Path.GetTempPath(), "Memoria.Tests") : AppPaths.DataDirectory;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "memoria.log");
            lock (Gate)
            {
                if (File.Exists(path) && new FileInfo(path).Length >= MaxLogBytes)
                    File.Move(path, path + ".old", overwrite: true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // 로깅 실패는 무시한다.
        }
    }
}
