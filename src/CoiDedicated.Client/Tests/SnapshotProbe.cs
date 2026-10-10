using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoiDedicated.Client;

internal static class SnapshotProbe
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var options = new Dictionary<string, string>();
            for (int i = 0; i < args.Length; i += 2) options.Add(args[i], args[i + 1]);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            if (options.ContainsKey("--live"))
            {
                using var session = await LiveSession.JoinAsync("127.0.0.1", int.Parse(options["--port"]),
                    options["--secret-file"], options["--output"], options.ContainsKey("--mismatch") ? "CoI 0.8.7d / different" : "CoI 0.8.7d / fixture", cancellation.Token);
                session.SendReady(session.SnapshotStep);
                var tick = await WaitTick(session, cancellation.Token);
                if (tick.Sequence != 17 || tick.BeforeStep != 123 || tick.AfterStep != 124 || tick.Paused || tick.Commands.Length != 1)
                    throw new InvalidOperationException("First live tick fixture differs.");
                if (session.Presence.Length != 1 || session.Presence[0].Name != "fixture-other" || session.Presence[0].PeerId != 7)
                    throw new InvalidOperationException("Presence did not remain separate from authoritative ticks.");
                session.Propose(1, new byte[] { 1, 2, 3 });
                session.Acknowledge(tick.Sequence, tick.AfterStep);
                if (options.ContainsKey("--resync"))
                {
                    while (session.Failure == null) await Task.Delay(5, cancellation.Token);
                    if (!session.RecoveryRequired || !session.Failure.Contains("fixture checkpoint mismatch"))
                        throw new InvalidOperationException("Server recovery signal was lost.");
                    Console.WriteLine("Explicit server recovery signal preserved.");
                    return 0;
                }
                var second = await WaitTick(session, cancellation.Token);
                if (second.Sequence != 18 || second.BeforeStep != 124 || second.AfterStep != 124 || !second.Paused)
                    throw new InvalidOperationException("Second live tick fixture differs.");
                if (!session.Rejections.TryDequeue(out var rejection) || rejection.RequestId != 1 || session.Failure != null)
                    throw new InvalidOperationException("A rejected command must be reported without stopping ticks.");
                Console.WriteLine("Live session authenticated, downloaded, readied, proposed, and acknowledged ticks.");
                return 0;
            }
            int step = await SnapshotDownload.DownloadAsync("127.0.0.1", int.Parse(options["--port"]),
                options["--secret-file"], options["--output"], "CoI 0.8.7d / fixture", cancellation.Token);
            Console.WriteLine($"Downloaded snapshot at step {step}");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }

    private static async Task<LiveTick> WaitTick(LiveSession session, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            if (session.Ticks.TryDequeue(out var tick)) return tick;
            if (session.Failure != null) throw new InvalidOperationException(session.Failure);
            await Task.Delay(5, cancellation);
        }
        throw new OperationCanceledException();
    }
}
