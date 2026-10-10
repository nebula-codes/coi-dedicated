using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace CoiDedicated.Peer;

internal static class Program
{
    public static int Main(string[] args)
    {
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            PeerOptions options = PeerOptions.Parse(args);
            if (options.Help) { PeerOptions.PrintHelp(); return 0; }
            string managed = Path.Combine(options.GameDirectory, "Captain of Industry_Data", "Managed");
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                if (name.Name is not ("Mafi" or "Mafi.Core" or "Mafi.Base")) return null;
                string path = Path.Combine(managed, name.Name + ".dll");
                return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
            };
            Directory.CreateDirectory(options.DataDirectory);
            Environment.CurrentDirectory = options.GameDirectory;
            return RunPeer(options, stop.Token);
        }
        catch (Exception error)
        {
            while (error is TargetInvocationException { InnerException: not null }) error = error.InnerException;
            Console.Error.WriteLine($"[peer] Failed: {error}");
            return 1;
        }
        finally { Console.CancelKeyPress -= handler; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunPeer(PeerOptions options, CancellationToken cancellationToken) => PeerSession.Run(options, cancellationToken);
}
