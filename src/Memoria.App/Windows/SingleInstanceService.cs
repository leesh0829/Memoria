// src/Memoria.App/Windows/SingleInstanceService.cs
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Memoria.App.Services;

namespace Memoria.App.Windows;

public interface ISingleInstanceService : IDisposable
{
    bool TryAcquire();
    bool SignalExistingInstance(PipeCommand command);
    event EventHandler<PipeCommand>? CommandReceived;
}

public sealed class SingleInstanceService : ISingleInstanceService
{
    private const string DefaultMutexName = "Memoria.SingleInstance.Mutex";
    private const string DefaultPipeName = "Memoria.SingleInstance.Pipe";

    private readonly string _mutexName;
    private readonly string _pipeName;
    private Mutex? _mutex;
    private CancellationTokenSource? _cts;
    private Task? _serverLoop;

    public event EventHandler<PipeCommand>? CommandReceived;

    public SingleInstanceService() : this(DefaultMutexName, DefaultPipeName) { }

    public SingleInstanceService(string mutexName, string pipeName)
    {
        _mutexName = mutexName;
        _pipeName = pipeName;
    }

    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, _mutexName, out bool createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        _cts = new CancellationTokenSource();
        _serverLoop = Task.Run(() => ServerLoopAsync(_cts.Token));
        return true;
    }

    private async Task ServerLoopAsync(CancellationToken token)
    {
        var accessDeniedReported = false;
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                accessDeniedReported = false;
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                using var reader = new StreamReader(server, leaveOpen: true);
                string? line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (line is not null && PipeMessage.TryParse(line, out var command))
                {
                    // 구독자가 반환했다는 것은 UI Dispatcher가 실제로 명령을 처리했다는 뜻이다.
                    // 단순 pipe 연결 성공만으로 좀비 인스턴스를 정상으로 오판하지 않는다.
                    CommandReceived?.Invoke(this, command);
                    using var writer = new StreamWriter(server, System.Text.Encoding.UTF8, 1024, leaveOpen: true);
                    await writer.WriteLineAsync("ok").ConfigureAwait(false);
                    await writer.FlushAsync(token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellation requested (during accept or read): stop accepting cleanly.
                break;
            }
            catch (IOException)
            {
                // A single broken/aborted client connection must not kill the server;
                // dispose this stream and keep accepting the next connection.
                continue;
            }
            catch (UnauthorizedAccessException ex)
            {
                // 이전 버전/다른 권한 수준의 프로세스가 같은 이름의 파이프를 잠시
                // 점유해도 서버 작업이 영구 종료되지 않도록 기록 후 재시도한다.
                if (!accessDeniedReported)
                {
                    AppLog.Warn($"SingleInstance server access denied: 0x{ex.HResult:X8}");
                    accessDeniedReported = true;
                }
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SingleInstance.ServerLoop", ex);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    public bool SignalExistingInstance(PipeCommand command)
    {
        // 두 번째 실행은 UI 시작 스레드에서 이 경로를 거친다. 기존 2초 동기 대기와
        // 서버가 먼저 닫을 때의 Dispose 예외가 앱을 '응답 없음'처럼 보이게 했다.
        // 전달은 best-effort이며 실패해도 두 번째 인스턴스는 조용히 종료하면 된다.
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
            client.Connect(250);
            using var writer = new StreamWriter(client, System.Text.Encoding.UTF8, 1024, leaveOpen: true);
            writer.WriteLine(PipeMessage.Serialize(command));
            writer.Flush();
            using var reader = new StreamReader(client, leaveOpen: true);
            var acknowledgement = reader.ReadLineAsync();
            return acknowledgement.Wait(TimeSpan.FromSeconds(2))
                && string.Equals(acknowledgement.Result, "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (TimeoutException)
        {
            // 기존 인스턴스가 종료 중이거나 아직 pipe를 열지 못한 짧은 경합.
            return false;
        }
        catch (IOException)
        {
            // 서버가 명령을 받은 직후 pipe를 닫아도 사용자에게 오류를 노출하지 않는다.
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            // 관리자 설치 직후 실행된 인스턴스와 일반 권한 시작프로그램 인스턴스가
            // 섞여도 두 번째 프로세스의 OnStartup을 중단시키지 않는다.
            AppLog.Warn($"SingleInstance pipe access denied: 0x{ex.HResult:X8}");
            return false;
        }
        catch (AggregateException)
        {
            // acknowledgement 대기 중 서버가 종료되거나 UI 응답 제한 시간을 넘긴 경우.
            return false;
        }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* shutdown best-effort */ }
        // 종료 UI를 최대 1초간 붙잡지 않는다. 파이프는 cancellation 이후 자체 정리되며
        // 50ms 안에 끝나지 않아도 프로세스 종료에 맡겨도 안전하다.
        try { _serverLoop?.Wait(TimeSpan.FromMilliseconds(50)); } catch { /* best-effort */ }
        _cts?.Dispose();
        _mutex?.Dispose();
        _mutex = null;
    }
}
