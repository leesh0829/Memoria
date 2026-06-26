// src/Memoria.App/Windows/SingleInstanceService.cs
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace Memoria.App.Windows;

public interface ISingleInstanceService : IDisposable
{
    bool TryAcquire();
    void SignalExistingInstance(PipeCommand command);
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
        while (!token.IsCancellationRequested)
        {
            using var server = new NamedPipeServerStream(
                _pipeName, PipeDirection.In, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try
            {
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            using var reader = new StreamReader(server);
            string? line = await reader.ReadLineAsync(token).ConfigureAwait(false);
            if (line is not null && PipeMessage.TryParse(line, out var command))
                CommandReceived?.Invoke(this, command);
        }
    }

    public void SignalExistingInstance(PipeCommand command)
    {
        using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
        client.Connect(2000);
        using var writer = new StreamWriter(client) { AutoFlush = true };
        writer.WriteLine(PipeMessage.Serialize(command));
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* shutdown best-effort */ }
        _cts?.Dispose();
        _mutex?.Dispose();
        _mutex = null;
    }
}
