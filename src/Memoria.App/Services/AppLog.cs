using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Memoria.App.Services;

/// <summary>
/// 오류와 성능 진단을 UI 스레드에서 파일로 직접 쓰지 않고 전용 백그라운드 스레드에서
/// 묶어서 기록한다. 성능 범위는 상관관계 ID/부모 ID와 함께 활성 작업 목록에도 남아,
/// UI 정지 시 "그 순간 실행 중이던 작업"을 확인할 수 있다.
/// </summary>
public static class AppLog
{
    private const long MaxLogBytes = 4 * 1024 * 1024;
    private const int MaxQueuedEntries = 4096;
    private const int MaxRecentEntries = 32;
    private const int MaxRecentCharacters = 384;
    private const int MaxLineCharacters = 16 * 1024;
    private const int MaxBatchCharacters = 256 * 1024;

    private static readonly ConcurrentQueue<LogEntry> Entries = new();
    private static readonly AutoResetEvent QueueSignal = new(false);
    private static readonly object WorkerGate = new();
    private static readonly object RecentGate = new();
    private static readonly Queue<string> Recent = new();
    private static readonly ConcurrentDictionary<long, ActiveOperation> Active = new();
    private static readonly AsyncLocal<long?> CurrentOperation = new();

    private static Thread? _worker;
    private static int _queuedCount;
    private static int _writerActive;
    private static int _droppedCount;
    private static long _nextOperationId;

    public static void Error(string source, Exception ex)
        => Enqueue(LogKind.Application, $"[ERROR] source={Clean(source)} exception={Clean(ex.ToString())}");

    public static void Info(string message)
        => Enqueue(LogKind.Application, $"[INFO] {Clean(message)}");

    public static void Warn(string message)
        => Enqueue(LogKind.Application, $"[WARN] {Clean(message)}");

    /// <summary>사용자 동작의 시작점을 본문/검색어 없이 ID와 종류만 기록한다.</summary>
    public static void Action(string name, string? details = null)
    {
        var text = $"action={Clean(name)} thread={Environment.CurrentManagedThreadId}";
        if (!string.IsNullOrWhiteSpace(details)) text += $" details={Clean(details)}";
        Remember($"ACTION {text}");
        Enqueue(LogKind.Performance, $"[ACTION] {text}");
    }

    /// <summary>
    /// 런타임/Dispatcher 진단은 recent 스냅샷에 다시 넣지 않는다. 진단 메시지가
    /// 자신의 이전 스냅샷을 재귀적으로 포함해 메모리가 기하급수적으로 커지는 것을 막는다.
    /// </summary>
    public static void Diagnostic(string name, string details)
        => Enqueue(LogKind.Performance,
            $"[DIAG] name={Clean(name)} thread={Environment.CurrentManagedThreadId} details={Clean(details)}");

    /// <summary>Core SQLite 진단 훅의 수신점. 빠른 작업도 파일에는 남기되 최근 동작은 느린 DB 작업만 차지한다.</summary>
    public static void Database(string operation, double waitMs, double elapsedMs, int threadId)
    {
        var text = $"operation={Clean(operation)} wait_ms={waitMs:F1} elapsed_ms={elapsedMs:F1} thread={threadId}";
        if (waitMs >= 25 || elapsedMs >= 50) Remember($"DB {text}");
        Enqueue(LogKind.Performance, $"[DB] {text}");
        if (waitMs >= 100 || elapsedMs >= 250)
            Enqueue(LogKind.Application, $"[DB] {text}");
    }

    /// <summary>
    /// 작업 종료 시간은 빠른 작업도 모두 성능 로그에 남긴다. warnAfterMs 이상이면
    /// 일반 로그에도 남겨 사용자가 공유할 파일 하나만 있어도 병목을 찾을 수 있게 한다.
    /// </summary>
    public static IDisposable Measure(string operation, int warnAfterMs = 250)
        => new PerformanceScope(operation, warnAfterMs);

    /// <summary>UI 정지 시 활성 작업과 직전 동작을 한 줄로 붙일 수 있는 스냅샷.</summary>
    public static string CaptureActivitySnapshot()
    {
        var now = Stopwatch.GetTimestamp();
        var active = Active.Values
            .OrderByDescending(x => Stopwatch.GetElapsedTime(x.StartedAt, now))
            .Take(8)
            .Select(x => $"{x.Id}:{x.Name}:{Stopwatch.GetElapsedTime(x.StartedAt, now).TotalMilliseconds:F0}ms:t{x.ThreadId}")
            .ToArray();
        string[] recent;
        lock (RecentGate) recent = Recent.TakeLast(12).ToArray();
        return $"active=[{string.Join('|', active)}] recent=[{string.Join('|', recent)}]";
    }

    /// <summary>종료 직전 대기열을 제한 시간 안에서 비운다.</summary>
    public static bool Flush(TimeSpan timeout)
    {
        try
        {
            EnsureWorker();
            QueueSignal.Set();
            var started = Stopwatch.GetTimestamp();
            while (Volatile.Read(ref _queuedCount) > 0 || Volatile.Read(ref _writerActive) != 0)
            {
                if (Stopwatch.GetElapsedTime(started) >= timeout) return false;
                Thread.Sleep(10);
            }
            return true;
        }
        catch { return false; }
    }

    private sealed class PerformanceScope : IDisposable
    {
        private readonly long _id;
        private readonly long? _parentId;
        private readonly string _operation;
        private readonly int _warnAfterMs;
        private readonly long _started = Stopwatch.GetTimestamp();
        private readonly int _startedThread = Environment.CurrentManagedThreadId;
        private int _finished;

        public PerformanceScope(string operation, int warnAfterMs)
        {
            _operation = Clean(operation);
            _warnAfterMs = warnAfterMs;
            _parentId = CurrentOperation.Value;
            _id = Interlocked.Increment(ref _nextOperationId);
            CurrentOperation.Value = _id;
            Active[_id] = new ActiveOperation(_id, _parentId, _operation, _started, _startedThread);
            Remember($"START {_id}:{_operation}:t{_startedThread}");
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _finished, 1) != 0) return;
            Active.TryRemove(_id, out _);
            if (CurrentOperation.Value == _id) CurrentOperation.Value = _parentId;

            var elapsed = Stopwatch.GetElapsedTime(_started);
            var parent = _parentId?.ToString() ?? "-";
            var line = $"operation={_operation} id={_id} parent={parent}" +
                       $" elapsed_ms={elapsed.TotalMilliseconds:F1}" +
                       $" start_thread={_startedThread} end_thread={Environment.CurrentManagedThreadId}";
            Remember($"END {_id}:{_operation}:{elapsed.TotalMilliseconds:F0}ms");
            Enqueue(LogKind.Performance, $"[PERF] {line}");
            if (elapsed.TotalMilliseconds >= _warnAfterMs)
                Enqueue(LogKind.Application, $"[PERF] {line}");
        }
    }

    private static void Remember(string value)
    {
        lock (RecentGate)
        {
            Recent.Enqueue(Clean(value, MaxRecentCharacters));
            while (Recent.Count > MaxRecentEntries) Recent.Dequeue();
        }
    }

    private static void Enqueue(LogKind kind, string line)
    {
        try
        {
            EnsureWorker();
            line = Clean(line, MaxLineCharacters);
            var count = Interlocked.Increment(ref _queuedCount);
            if (count > MaxQueuedEntries)
            {
                Interlocked.Decrement(ref _queuedCount);
                Interlocked.Increment(ref _droppedCount);
                return;
            }
            Entries.Enqueue(new LogEntry(kind, DateTimeOffset.Now, line));
            if (count == 1) QueueSignal.Set();
        }
        catch
        {
            // 진단 실패는 앱 동작에 영향을 주지 않는다.
        }
    }

    private static void EnsureWorker()
    {
        if (_worker is not null) return;
        lock (WorkerGate)
        {
            if (_worker is not null) return;
            _worker = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "Memoria diagnostic log writer",
                Priority = ThreadPriority.BelowNormal,
            };
            _worker.Start();
        }
    }

    private static void WriterLoop()
    {
        while (true)
        {
            QueueSignal.WaitOne(TimeSpan.FromSeconds(1));
            try
            {
                Interlocked.Exchange(ref _writerActive, 1);
                DrainQueue();
            }
            catch { /* 로깅 실패는 무시하고 다음 묶음에서 재시도 */ }
            finally { Interlocked.Exchange(ref _writerActive, 0); }
        }
    }

    private static void DrainQueue()
    {
        if (Entries.IsEmpty) return;
        var app = new StringBuilder();
        var perf = new StringBuilder();
        var dropped = Interlocked.Exchange(ref _droppedCount, 0);
        if (dropped > 0)
            perf.AppendLine($"{DateTimeOffset.Now:O} [DIAG] dropped_log_entries={dropped}");

        while (app.Length + perf.Length < MaxBatchCharacters && Entries.TryDequeue(out var entry))
        {
            Interlocked.Decrement(ref _queuedCount);
            var target = entry.Kind == LogKind.Application ? app : perf;
            target.Append(entry.Timestamp.ToString("O"));
            target.Append(' ');
            target.AppendLine(entry.Line);
        }

        var directory = ResolveLogDirectory();
        Directory.CreateDirectory(directory);
        if (app.Length > 0) AppendBatch(Path.Combine(directory, "memoria.log"), app.ToString());
        if (perf.Length > 0) AppendBatch(Path.Combine(directory, "memoria-performance.log"), perf.ToString());
        if (!Entries.IsEmpty) QueueSignal.Set();
    }

    private static string ResolveLogDirectory()
    {
        var processName = Process.GetCurrentProcess().ProcessName;
        return processName.Contains("testhost", StringComparison.OrdinalIgnoreCase)
               || processName.Contains("vstest", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(Path.GetTempPath(), "Memoria.Tests")
            : AppPaths.DataDirectory;
    }

    private static void AppendBatch(string path, string text)
    {
        var oldPath = path + ".old";
        TrimToTail(oldPath, MaxLogBytes);
        var incomingBytes = Encoding.UTF8.GetByteCount(text);
        if (File.Exists(path) && new FileInfo(path).Length + incomingBytes > MaxLogBytes)
        {
            File.Move(path, oldPath, overwrite: true);
            TrimToTail(oldPath, MaxLogBytes);
        }
        File.AppendAllText(path, text, Encoding.UTF8);
    }

    private static void TrimToTail(string path, long maxBytes)
    {
        if (!File.Exists(path)) return;
        var info = new FileInfo(path);
        if (info.Length <= maxBytes) return;

        var temp = path + ".trim";
        try
        {
            using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var target = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                source.Seek(-maxBytes, SeekOrigin.End);
                source.CopyTo(target);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static string Clean(string? value, int maxCharacters = MaxLineCharacters)
    {
        var cleaned = (value ?? "")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('|', '/');
        return cleaned.Length <= maxCharacters
            ? cleaned
            : cleaned[..maxCharacters] + "…[truncated]";
    }

    private enum LogKind { Application, Performance }
    private sealed record LogEntry(LogKind Kind, DateTimeOffset Timestamp, string Line);
    private sealed record ActiveOperation(long Id, long? ParentId, string Name, long StartedAt, int ThreadId);
}
