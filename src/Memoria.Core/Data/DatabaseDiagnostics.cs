using System.Diagnostics;
using System.Threading;

namespace Memoria.Core.Data;

/// <summary>
/// Core가 UI 프로젝트를 참조하지 않고도 SQLite 작업 시간과 단일 writer 락 대기를
/// 진단 로그로 전달하기 위한 매우 가벼운 훅. Sink가 없으면 사실상 Stopwatch 비용뿐이다.
/// </summary>
public static class DatabaseDiagnostics
{
    public static Action<string, double, double, int>? OperationCompleted { get; set; }

    public static IDisposable Measure(string operation) => new ReadScope(operation);

    public static IDisposable EnterWrite(object gate, string operation)
    {
        var waitStarted = Stopwatch.GetTimestamp();
        Monitor.Enter(gate);
        return new WriteScope(gate, operation, waitStarted, Stopwatch.GetTimestamp());
    }

    private sealed class ReadScope(string operation) : IDisposable
    {
        private readonly long _started = Stopwatch.GetTimestamp();
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            SafeReport(operation, 0, Stopwatch.GetElapsedTime(_started).TotalMilliseconds);
        }
    }

    private sealed class WriteScope(
        object gate,
        string operation,
        long waitStarted,
        long acquiredAt) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            var holdMs = Stopwatch.GetElapsedTime(acquiredAt).TotalMilliseconds;
            var waitMs = Stopwatch.GetElapsedTime(waitStarted, acquiredAt).TotalMilliseconds;
            Monitor.Exit(gate);
            SafeReport(operation, waitMs, holdMs);
        }
    }

    private static void SafeReport(string operation, double waitMs, double elapsedMs)
    {
        try { OperationCompleted?.Invoke(operation, waitMs, elapsedMs, Environment.CurrentManagedThreadId); }
        catch { /* 진단 훅은 데이터 작업을 방해하지 않는다. */ }
    }
}
