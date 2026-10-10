using System.Reflection;
using System.Runtime.Loader;
using System.Diagnostics;

namespace CoiDedicated;

internal static class Program
{
    public static int Main(string[] args)
    {
        using var shutdown = new ShutdownSignal();
        StreamWriter? logWriter = null;
        TextWriter originalOut = Console.Out, originalError = Console.Error;
        try
        {
            var options = HostOptions.Parse(args);
            if (options.Help) { HostOptions.PrintHelp(); return 0; }
            if (options.LogFile is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(options.LogFile)!);
                logWriter = new StreamWriter(new FileStream(options.LogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
                TextWriter synchronized = TextWriter.Synchronized(logWriter);
                Console.SetOut(synchronized);
                Console.SetError(synchronized);
                Console.WriteLine($"[host] Starting dedicated server at {DateTimeOffset.UtcNow:O}");
            }
            string managed = Path.Combine(options.GameDirectory, "Captain of Industry_Data", "Managed");
            foreach (string name in new[] { "Mafi", "Mafi.Core", "Mafi.Base" })
                if (!File.Exists(Path.Combine(managed, name + ".dll")))
                    throw new FileNotFoundException($"Missing {name}.dll. Supply --game-dir with the game installation.");
            Directory.CreateDirectory(options.DataDirectory);
            using FileStream? serverLease = options.Serve && !options.Inspect
                ? new FileStream(Path.Combine(options.DataDirectory, ".server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)
                : null;
            if (options.Serve && !options.Inspect)
                options = PrepareServerWorld(options, shutdown.Token);
            // Bind only the game-owned assemblies. The host supplies its own .NET runtime.
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                if (name.Name is not ("Mafi" or "Mafi.Core" or "Mafi.Base")) return null;
                return AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed, name.Name + ".dll"));
            };
            Environment.CurrentDirectory = options.GameDirectory;
            shutdown.Token.ThrowIfCancellationRequested();
            return GameHost.Run(options, cancellationToken: shutdown.Token);
        }
        catch (OperationCanceledException error)
        {
            Console.Error.WriteLine(error.Message);
            return shutdown.Token.IsCancellationRequested ? 0 : 130;
        }
        catch (Exception error)
        {
            while (error is TargetInvocationException { InnerException: not null }) error = error.InnerException;
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            logWriter?.Dispose();
        }
    }

    private static HostOptions PrepareServerWorld(HostOptions options, CancellationToken cancellationToken)
    {
        // The shipped loader repairs new-world terrain queues and props. Run
        // those repairs in separate processes before any client can join, then
        // load the repaired save in this process. No game assemblies, control
        // credentials, or listeners are needed in this launcher phase.
        string staging = Path.Combine(options.DataDirectory, ".bootstrap");
        string? source = options.Load;
        if (source is null)
        {
            string initialDirectory = Path.Combine(staging, "new");
            RunBootstrap(options, initialDirectory, load: null, cancellationToken);
            source = Path.Combine(initialDirectory, "island.save");
        }

        string canonicalDirectory = Path.Combine(staging, "canonical");
        RunBootstrap(options, canonicalDirectory, source, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        string canonical = Path.Combine(canonicalDirectory, "island.save");
        string output = Path.Combine(options.DataDirectory, "island.save");
        string temporary = output + ".bootstrap.tmp";
        using (var input = File.OpenRead(canonical))
        using (var destination = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            input.CopyTo(destination);
            destination.Flush(flushToDisk: true);
        }
        if (File.Exists(output))
        {
            File.Delete(output + ".previous");
            File.Replace(temporary, output, output + ".previous", ignoreMetadataErrors: true);
        }
        else File.Move(temporary, output);
        Console.WriteLine("[bootstrap] Canonical save ready; starting the dedicated simulation.");
        return options with { Load = output };
    }

    private static void RunBootstrap(HostOptions options, string dataDirectory, string? load, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(dataDirectory);
        string processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the running host executable.");
        var start = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (string argument in new[] { "--game-dir", options.GameDirectory, "--data-dir", dataDirectory, "--map", options.Map, "--steps", "0" })
            start.ArgumentList.Add(argument);
        if (options.StartingFactory) start.ArgumentList.Add("--starting-factory");
        if (load is not null)
        {
            start.ArgumentList.Add("--load");
            start.ArgumentList.Add(load);
        }
        string logPath = Path.Combine(dataDirectory, "bootstrap.log");
        string errorPath = Path.Combine(dataDirectory, "bootstrap-errors.log");
        Console.WriteLine($"[bootstrap] {(load is null ? "Creating island" : "Loading and canonicalizing island")}; log: {logPath}");
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start the world bootstrap process.");
        using var log = File.Create(logPath);
        using var errors = File.Create(errorPath);
        Task stdout = CopyBootstrapOutput(child.StandardOutput, log);
        Task stderr = CopyBootstrapOutput(child.StandardError, errors);
        using var interruption = cancellationToken.Register(() =>
        {
            try { if (!child.HasExited) child.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        child.WaitForExit();
        Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
        cancellationToken.ThrowIfCancellationRequested();
        if (child.ExitCode != 0)
            throw new InvalidOperationException($"World bootstrap exited with code {child.ExitCode}. See {logPath} and {errorPath}.");
        if (!File.Exists(Path.Combine(dataDirectory, "island.save")))
            throw new InvalidOperationException($"World bootstrap did not produce a save. See {logPath}.");
    }

    private static async Task CopyBootstrapOutput(StreamReader source, Stream destination)
    {
        using var writer = new StreamWriter(destination, new System.Text.UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        while (await source.ReadLineAsync() is { } line)
        {
            await writer.WriteLineAsync(line);
            Console.WriteLine("[bootstrap] " + line);
        }
    }
}
