using System.Runtime.InteropServices;

namespace CoiDedicated;

// Keep one cancellation lifetime across world bootstrap, loading, and serving.
// Docker sends SIGTERM; Windows consoles continue to use Ctrl+C.
internal sealed class ShutdownSignal : IDisposable
{
    private readonly CancellationTokenSource source = new();
    private readonly PosixSignalRegistration? termination;
    private readonly ConsoleCancelEventHandler consoleHandler;

    public CancellationToken Token => source.Token;

    public ShutdownSignal()
    {
        consoleHandler = (_, context) => { context.Cancel = true; source.Cancel(); };
        Console.CancelKeyPress += consoleHandler;
        if (!OperatingSystem.IsWindows())
            termination = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                source.Cancel();
            });
    }

    public void Dispose()
    {
        termination?.Dispose();
        Console.CancelKeyPress -= consoleHandler;
        source.Dispose();
    }
}
