using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Threading;

namespace Memoria.App.Services;

/// <summary>
/// 주기적으로 Dispatcher에 가벼운 작업을 넣어 UI 메시지 처리가 장시간 멈춘 경우를 기록한다.
/// 한 번에 하나의 probe만 대기시켜 멈춘 동안 큐가 불어나지 않게 한다.
/// </summary>
public sealed class UiResponsivenessMonitor : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Timer _timer;
    private readonly TimeSpan _stallThreshold;
    private readonly object _sampleGate = new();
    private long _postedAt;
    private int _probePending;
    private int _disposed;
    private long _lastReportedAt;
    private int _suppressedReports;
    private int _stallReported;
    private long _lastRuntimeSampleAt;
    private long _lastCpuSampleAt;
    private TimeSpan _lastCpuTime;

    public UiResponsivenessMonitor(
        Dispatcher dispatcher,
        TimeSpan? interval = null,
        TimeSpan? stallThreshold = null)
    {
        _dispatcher = dispatcher;
        _stallThreshold = stallThreshold ?? TimeSpan.FromSeconds(1.5);
        var cadence = interval ?? TimeSpan.FromSeconds(1);
        using (var process = Process.GetCurrentProcess())
            _lastCpuTime = process.TotalProcessorTime;
        _lastCpuSampleAt = Stopwatch.GetTimestamp();
        _lastRuntimeSampleAt = _lastCpuSampleAt;
        _timer = new Timer(_ => PostProbe(), null, cadence, cadence);
    }

    private void PostProbe()
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        if (Volatile.Read(ref _probePending) != 0)
        {
            var elapsed = Stopwatch.GetElapsedTime(Volatile.Read(ref _postedAt));
            if (elapsed >= _stallThreshold) ReportStall(elapsed, ongoing: true);
            SampleRuntimeIfDue();
            return;
        }

        if (Interlocked.CompareExchange(ref _probePending, 1, 0) != 0) return;

        Volatile.Write(ref _postedAt, Stopwatch.GetTimestamp());
        Interlocked.Exchange(ref _stallReported, 0);
        try
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(CompleteProbe));
        }
        catch
        {
            Interlocked.Exchange(ref _probePending, 0);
        }
        SampleRuntimeIfDue();
    }

    private void CompleteProbe()
    {
        var elapsed = Stopwatch.GetElapsedTime(_postedAt);
        Interlocked.Exchange(ref _probePending, 0);
        if (Volatile.Read(ref _disposed) != 0 || elapsed < _stallThreshold) return;

        if (Interlocked.Exchange(ref _stallReported, 1) == 0)
            ReportStall(elapsed, ongoing: false);
        else
            AppLog.Diagnostic("UIThread.Recovered", $"elapsed_ms={elapsed.TotalMilliseconds:F0}");
    }

    private void ReportStall(TimeSpan elapsed, bool ongoing)
    {
        Interlocked.Exchange(ref _stallReported, 1);

        // 30초 이상도 절전이라고 단정하지 않는다. 별도의 System.PowerMode 시각과
        // 대조해야 실제 장기 정지와 절전 복귀를 구분할 수 있다.
        var message = elapsed >= TimeSpan.FromSeconds(30)
            ? $"UIThread.LongStall elapsed_ms={elapsed.TotalMilliseconds:F0}"
            : $"UIThread.Stall elapsed_ms={elapsed.TotalMilliseconds:F0}";
        var now = Stopwatch.GetTimestamp();
        if (_lastReportedAt != 0
            && Stopwatch.GetElapsedTime(_lastReportedAt, now) < TimeSpan.FromSeconds(30))
        {
            _suppressedReports++;
            return;
        }
        var suppressed = _suppressedReports;
        _suppressedReports = 0;
        _lastReportedAt = now;
        if (suppressed > 0) message += $" suppressed={suppressed}";
        var activity = AppLog.CaptureActivitySnapshot();
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var runtime = CaptureRuntimeSnapshot();
            AppLog.Warn($"{message} ongoing={ongoing} {runtime} {activity}");
            AppLog.Diagnostic("UIThread.StallDiagnostic", $"elapsed_ms={elapsed.TotalMilliseconds:F0} ongoing={ongoing} {runtime} {activity}");
        });
    }

    private void SampleRuntimeIfDue()
    {
        var now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(Volatile.Read(ref _lastRuntimeSampleAt), now) < TimeSpan.FromMinutes(1)) return;
        Volatile.Write(ref _lastRuntimeSampleAt, now);
        var activity = AppLog.CaptureActivitySnapshot();
        AppLog.Diagnostic("Runtime.Sample", $"{CaptureRuntimeSnapshot()} {activity}");
    }

    private string CaptureRuntimeSnapshot()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var now = Stopwatch.GetTimestamp();
            double cpuPercent;
            lock (_sampleGate)
            {
                var cpu = process.TotalProcessorTime;
                var wall = Stopwatch.GetElapsedTime(_lastCpuSampleAt, now);
                cpuPercent = wall.TotalMilliseconds <= 0
                    ? 0
                    : (cpu - _lastCpuTime).TotalMilliseconds / wall.TotalMilliseconds
                      / Math.Max(1, Environment.ProcessorCount) * 100;
                _lastCpuTime = cpu;
                _lastCpuSampleAt = now;
            }

            ThreadPool.GetAvailableThreads(out var workerAvailable, out var ioAvailable);
            ThreadPool.GetMaxThreads(out var workerMax, out var ioMax);
            var gc = GC.GetGCMemoryInfo();
            var dbBytes = FileSize(AppPaths.DatabaseFile);
            var walBytes = FileSize(AppPaths.DatabaseFile + "-wal");
            return $"pid={process.Id} cpu_pct={cpuPercent:F1}" +
                   $" working_set_mb={Mb(process.WorkingSet64):F1} private_mb={Mb(process.PrivateMemorySize64):F1}" +
                   $" managed_mb={Mb(GC.GetTotalMemory(forceFullCollection: false)):F1}" +
                   $" gc_heap_mb={Mb(gc.HeapSizeBytes):F1} gc_fragmented_mb={Mb(gc.FragmentedBytes):F1}" +
                   $" gc_pause_pct={gc.PauseTimePercentage:F1}" +
                   $" gc0={GC.CollectionCount(0)} gc1={GC.CollectionCount(1)} gc2={GC.CollectionCount(2)}" +
                   $" handles={process.HandleCount} threads={process.Threads.Count}" +
                   $" threadpool_threads={ThreadPool.ThreadCount} threadpool_pending={ThreadPool.PendingWorkItemCount}" +
                   $" worker_available={workerAvailable}/{workerMax} io_available={ioAvailable}/{ioMax}" +
                   $" db_mb={Mb(dbBytes):F2} wal_mb={Mb(walBytes):F2}";
        }
        catch (Exception ex)
        {
            return $"runtime_snapshot_error={ex.GetType().Name}";
        }
    }

    private static long FileSize(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return -1; }
    }

    private static double Mb(long bytes) => bytes / 1024d / 1024d;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _timer.Dispose();
    }
}
