using System.Diagnostics;
using System.Runtime.InteropServices;
using CoiDedicated;

internal static class Program
{
    private const int SigInt = 2;
    private const int SigTerm = 15;

    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "child") return await RunChild(args[1], args[2]);
        if (!OperatingSystem.IsLinux())
        {
            Console.WriteLine("SKIP: real POSIX signal tests require Linux. Windows Ctrl+C uses the unchanged Console.CancelKeyPress path.");
            return 0;
        }

        string scratch = Path.Combine(Path.GetTempPath(), "coi-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            await GracefulStop("SIGTERM", SigTerm, repeated: false, scratch);
            await GracefulStop("SIGINT", SigInt, repeated: false, scratch);
            await GracefulStop("repeated SIGTERM during finalization", SigTerm, repeated: true, scratch);
            await DisposedRegistration();
            Console.WriteLine("PASS: 4 lifecycle process tests");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            // This directory is created above with an unpredictable test-only name.
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static async Task GracefulStop(string description, int signal, bool repeated, string scratch)
    {
        string output = Path.Combine(scratch, Guid.NewGuid().ToString("N") + ".txt");
        using var child = StartChild("save", output);
        Task<string> errors = child.StandardError.ReadToEndAsync();
        try
        {
            await ExpectLine(child, "READY");
            SendSignal(child, signal);
            await ExpectLine(child, "FINALIZING");
            if (repeated) SendSignal(child, signal);
            await child.StandardInput.WriteLineAsync("finish");
            await child.StandardInput.FlushAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (child.ExitCode != 0) throw new Exception($"{description}: exit {child.ExitCode}; {await errors}");
            if (!File.Exists(output) || await File.ReadAllTextAsync(output) != "save completed; callbacks=1")
                throw new Exception($"{description}: finalization did not complete exactly once.");
            Console.WriteLine($"PASS: {description} permits final save and exits successfully");
        }
        finally { StopChild(child); }
    }

    private static async Task DisposedRegistration()
    {
        using var child = StartChild("disposed", "unused");
        Task<string> errors = child.StandardError.ReadToEndAsync();
        try
        {
            await ExpectLine(child, "READY");
            SendSignal(child, SigTerm);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (child.ExitCode != 128 + SigTerm)
                throw new Exception($"Disposed handler did not restore SIGTERM behavior: exit {child.ExitCode}; {await errors}");
            Console.WriteLine("PASS: disposing the signal lifetime restores normal SIGTERM termination");
        }
        finally { StopChild(child); }
    }

    private static async Task<int> RunChild(string mode, string output)
    {
        using var shutdown = new ShutdownSignal();
        if (mode == "disposed")
        {
            shutdown.Dispose();
            Console.WriteLine("READY");
            await Task.Delay(TimeSpan.FromSeconds(30));
            return 2;
        }

        int callbacks = 0;
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = shutdown.Token.Register(() =>
        {
            Interlocked.Increment(ref callbacks);
            stopped.TrySetResult();
        });
        Console.WriteLine("READY");
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Console.WriteLine("FINALIZING");
        if (await Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)) != "finish") return 3;
        // Keep finalization active long enough for a second OS signal to arrive.
        await Task.Delay(250);
        using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        byte[] contents = System.Text.Encoding.UTF8.GetBytes($"save completed; callbacks={callbacks}");
        await file.WriteAsync(contents);
        file.Flush(flushToDisk: true);
        return 0;
    }

    private static Process StartChild(string mode, string output)
    {
        string executable = Environment.ProcessPath ?? throw new Exception("Cannot locate current process.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (string argument in new[] { "child", mode, output }) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new Exception("Could not start test child.");
    }

    private static async Task ExpectLine(Process child, string expected)
    {
        string? line = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
        if (line != expected) throw new Exception($"Expected '{expected}', received '{line ?? "EOF"}'.");
    }

    private static void SendSignal(Process child, int signal)
    {
        if (Kill(child.Id, signal) != 0)
            throw new Exception($"kill({child.Id}, {signal}) failed with errno {Marshal.GetLastPInvokeError()}.");
    }

    private static void StopChild(Process child)
    {
        if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(); }
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);
}
