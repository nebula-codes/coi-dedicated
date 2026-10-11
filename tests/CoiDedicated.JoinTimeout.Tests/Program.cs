using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Loader;
using CoiDedicated.Protocol;

// This is a transport/lifecycle test, not a game simulation or determinism test.
// It opens its own ephemeral loopback listener and sends only synthetic snapshot
// bytes. No running game, island, server profile, or real join key is accessed.
// Usage: dotnet run --project tests/CoiDedicated.JoinTimeout.Tests -c Release -- HOST_DLL GAME_DIR
if (args.Length != 2)
    throw new ArgumentException("Expected a built/packaged CoiDedicated.dll path and the game directory.");
string hostPath = Path.GetFullPath(args[0]);
string hostDirectory = Path.GetDirectoryName(hostPath)!;
string gameManaged = Path.Combine(Path.GetFullPath(args[1]), "Captain of Industry_Data", "Managed");
// The transport helpers use the test project's protocol reference. Independently
// check the release's protocol assembly so a stale packaged DLL cannot be hidden
// by that reference when the real host resolves its protocol dependency below.
var packagedContext = new AssemblyLoadContext("packaged-join-timing", isCollectible: true);
try
{
    Assembly packagedProtocol = packagedContext.LoadFromAssemblyPath(Path.Combine(hostDirectory, "CoiDedicated.Protocol.dll"));
    Type packagedTiming = packagedProtocol.GetType("CoiDedicated.Protocol.GameSessionTiming", throwOnError: true)!;
    foreach ((string name, TimeSpan expected) in new[]
    {
        (nameof(GameSessionTiming.JoinTimeout), GameSessionTiming.JoinTimeout),
        (nameof(GameSessionTiming.PeerIdleTimeout), GameSessionTiming.PeerIdleTimeout),
        (nameof(GameSessionTiming.ClientJoinDownloadTimeout), GameSessionTiming.ClientJoinDownloadTimeout)
    })
        Check((TimeSpan)packagedTiming.GetField(name)!.GetValue(null)! == expected, "Packaged protocol has stale timing: " + name);
    Console.WriteLine("PASS packaged protocol carries matching join, idle, and download deadlines.");
}
finally { packagedContext.Unload(); }
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string? directory = name.Name is "Mafi" or "Mafi.Core" or "Mafi.Base" ? gameManaged
        : name.Name is "CoiDedicated.GameAdapter" ? hostDirectory : null;
    return directory == null ? null : AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(directory, name.Name + ".dll"));
};
Assembly host = AssemblyLoadContext.Default.LoadFromAssemblyPath(hostPath);
Type sessionType = host.GetType("CoiDedicated.GameSessionServer", true)!;
Type peerType = sessionType.GetNestedType("Peer", BindingFlags.NonPublic)!;
Type admittedType = host.GetType("CoiDedicated.AdmittedCommand", true)!;
MethodInfo pumpMethod = sessionType.GetMethod("Pump")!;
Type simulationType = pumpMethod.GetParameters()[1].ParameterType;
object simulation = DispatchProxy.Create(simulationType, typeof(InertSimulationProxy));
const string secret = "isolated-join-timeout-test-secret";
string snapshotPath = Path.Combine(Path.GetTempPath(), "coi-join-timeout-" + Guid.NewGuid().ToString("N") + ".bin");
await File.WriteAllBytesAsync(snapshotPath, "synthetic snapshot for timeout transport testing"u8.ToArray());
try
{
    using var session = (IDisposable)Activator.CreateInstance(sessionType, "127.0.0.1", 0, secret, "timeout-regression")!;
    var listener = (TcpListener)Field(sessionType, "listener").GetValue(session)!;
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var worldPeers = (IList)Field(sessionType, "worldPeers").GetValue(session)!;
    int PlayerCount() => (int)sessionType.GetProperty("PlayerCount")!.GetValue(session)!;
    bool Waiting() => (bool)sessionType.GetProperty("WaitingForJoin")!.GetValue(session)!;
    void Pump() => pumpMethod.Invoke(session, new object[] { (Func<string>)(() => snapshotPath), simulation });
    async Task PumpUntil(Func<bool> completed, TimeSpan timeout, params TestPeer[] clients)
    {
        Stopwatch wait = Stopwatch.StartNew();
        while (!completed())
        {
            foreach (TestPeer client in clients) client.ThrowIfFailed();
            if (wait.Elapsed > timeout) throw new TimeoutException("Transport test condition was not reached.");
            Pump();
            await Task.Delay(20);
        }
        Pump();
        foreach (TestPeer client in clients) client.ThrowIfFailed();
    }
    async Task<TestPeer> Join(string name)
    {
        var client = new TestPeer(name);
        Task download = client.ConnectAndDownload(port, secret);
        try { await PumpUntil(() => download.IsCompleted, TimeSpan.FromSeconds(10), client); await download; return client; }
        catch { client.Dispose(); throw; }
    }

    // Exercise Pump's real boundary immediately. The peer is synthetic and is
    // not in the authenticated connection dictionary; it never owns a socket.
    object boundaryPeer = Activator.CreateInstance(peerType, new TcpClient(), "boundary-peer", 100U, CancellationToken.None)!;
    worldPeers.Add(boundaryPeer);
    Field(peerType, "JoinedAt").SetValue(boundaryPeer, DateTime.UtcNow - TimeSpan.FromSeconds(599));
    Pump();
    Check(Waiting() && worldPeers.Contains(boundaryPeer), "A loading peer was removed before 600 seconds.");
    Field(peerType, "JoinedAt").SetValue(boundaryPeer, DateTime.UtcNow - TimeSpan.FromSeconds(601));
    Pump();
    Check(!Waiting() && !worldPeers.Contains(boundaryPeer), "A loading peer survived past the 600-second boundary.");
    Console.WriteLine("PASS loading peer remains at 599 seconds and expires at 601 seconds.");

    Check(GameSessionTiming.JoinTimeout == TimeSpan.FromSeconds(600), "Join deadline must be ten minutes.");
    Check(GameSessionTiming.PeerIdleTimeout > GameSessionTiming.JoinTimeout, "Established peers must tolerate the entire join pause.");
    Check(GameSessionTiming.ClientJoinDownloadTimeout > GameSessionTiming.JoinTimeout, "Client download must not expire before server join.");
    Console.WriteLine("PASS client download and established-peer deadlines allow the full join window.");

    using TestPeer established = await Join("established-timeout-test");
    await established.SendReady();
    await PumpUntil(() => PlayerCount() == 1, TimeSpan.FromSeconds(5), established);
    using TestPeer loading = await Join("slow-loading-timeout-test");
    Check(Waiting() && PlayerCount() == 1, "Second join did not hold the simulation barrier.");
    Console.WriteLine("Waiting 85 seconds with one established player and one loading player; no client keepalives are sent.");
    Stopwatch pause = Stopwatch.StartNew();
    int nextProgress = 25;
    while (pause.Elapsed < TimeSpan.FromSeconds(85))
    {
        Pump();
        established.ThrowIfFailed();
        loading.ThrowIfFailed();
        Check(Waiting() && PlayerCount() == 1, "Loading or established peer expired while the island was loading.");
        if (pause.Elapsed.TotalSeconds >= nextProgress)
        {
            Console.WriteLine($"Still connected after {nextProgress} seconds.");
            nextProgress += 25;
        }
        await Task.Delay(20);
    }
    await loading.SendReady();
    await PumpUntil(() => PlayerCount() == 2 && !Waiting(), TimeSpan.FromSeconds(5), established, loading);
    Console.WriteLine("PASS Ready after 85 seconds releases the join barrier and retains the established peer.");

    // Avoid game state hashing: this test validates transport, and an inert
    // resolver cannot produce an actual world checkpoint. Publish otherwise
    // uses the production tick path and both peers return normal tick ACKs.
    foreach (object peer in worldPeers) Field(peerType, "NeedsInitialCheckpoint").SetValue(peer, false);
    Array commands = Array.CreateInstance(admittedType, 0);
    sessionType.GetMethod("Publish")!.Invoke(session, new object?[] { 0, 1, false, 1, commands, null });
    await PumpUntil(() => established.Ticks > 0 && loading.Ticks > 0, TimeSpan.FromSeconds(5), established, loading);
    await PumpUntil(() => worldPeers.Cast<object>().All(peer =>
    {
        object queue = Field(peerType, "AwaitingAck").GetValue(peer)!;
        return (int)queue.GetType().GetProperty("Count")!.GetValue(queue)! == 0;
    }), TimeSpan.FromSeconds(5), established, loading);
    Check(PlayerCount() == 2, "A peer failed after resumed ticks.");
    Console.WriteLine("PASS authoritative ticks resume and both players acknowledge the tick after the extended join.");
    Console.WriteLine($"PASS tested host {host.GetName().Version}; elapsed join pause {pause.Elapsed.TotalSeconds:F1}s.");
}
finally { File.Delete(snapshotPath); }

static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
    ?? throw new MissingFieldException(type.FullName, name);
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

public class InertSimulationProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? arguments)
    {
        if (targetMethod?.Name == "get_CurrentStep") return Activator.CreateInstance(targetMethod.ReturnType);
        throw new InvalidOperationException("Unexpected simulation call: " + targetMethod?.Name);
    }
}

sealed class TestPeer(string name) : IDisposable
{
    readonly TcpClient client = new();
    readonly CancellationTokenSource stop = new(TimeSpan.FromSeconds(110));
    Exception? failure;
    Task? receiveTask;
    int step;
    int ticks;
    public int Ticks => Volatile.Read(ref ticks);

    public async Task ConnectAndDownload(int port, string secret)
    {
        await client.ConnectAsync(IPAddress.Loopback, port, stop.Token);
        NetworkStream stream = client.GetStream();
        ProtocolFrame challenge = await Read();
        if (challenge.Kind != MessageKind.Welcome || challenge.Payload.Span[0] != 0)
            throw new InvalidOperationException("Expected authentication challenge.");
        var challengeReader = new PayloadReader(challenge.Payload[1..]);
        byte[] nonce = challengeReader.ReadBytes(32);
        challengeReader.ReadString(128);
        challengeReader.EnsureEnd();
        using (var hello = new PayloadWriter())
        {
            hello.WriteString(name, 64);
            hello.WriteBytes(ControlAuthentication.ComputeProof(secret, nonce, name), 32);
            await FrameCodec.WriteAsync(stream, hello.ToFrame(MessageKind.Hello), stop.Token);
        }
        ProtocolFrame welcome = await Read();
        if (welcome.Kind != MessageKind.Welcome || welcome.Payload.Span[0] != 1)
            throw new InvalidOperationException("Authentication was not accepted.");
        bool begun = false;
        while (true)
        {
            ProtocolFrame frame = await Read();
            if (frame.Kind == MessageKind.SnapshotBegin)
            {
                step = new PayloadReader(frame.Payload).ReadInt32();
                begun = true;
            }
            else if (frame.Kind == MessageKind.SnapshotChunk && begun) { }
            else if (frame.Kind == MessageKind.SnapshotEnd && begun) break;
            else throw new InvalidOperationException("Unexpected snapshot frame: " + frame.Kind);
        }
        receiveTask = Receive();
    }

    public async Task SendReady()
    {
        using var ready = new PayloadWriter();
        ready.WriteInt32(step);
        await FrameCodec.WriteAsync(client.GetStream(), ready.ToFrame(MessageKind.Ack), stop.Token);
    }

    async Task Receive()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                ProtocolFrame frame = await Read();
                if (frame.Kind == MessageKind.PresenceSnapshot) continue;
                if (frame.Kind != MessageKind.Tick) throw new InvalidOperationException("Unexpected active frame: " + frame.Kind);
                var tick = new PayloadReader(frame.Payload);
                ulong sequence = tick.ReadUInt64();
                tick.ReadInt32();
                int after = tick.ReadInt32();
                using var acknowledgment = new PayloadWriter();
                acknowledgment.WriteUInt64(sequence);
                acknowledgment.WriteInt32(after);
                await FrameCodec.WriteAsync(client.GetStream(), acknowledgment.ToFrame(MessageKind.Ack), stop.Token);
                Interlocked.Increment(ref ticks);
            }
        }
        catch (Exception error) when (!stop.IsCancellationRequested) { Volatile.Write(ref failure, error); }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) when (stop.IsCancellationRequested) { }
    }

    async Task<ProtocolFrame> Read() => await FrameCodec.ReadAsync(client.GetStream(), stop.Token)
        ?? throw new EndOfStreamException(name + " disconnected before the test completed.");
    public void ThrowIfFailed()
    {
        if (Volatile.Read(ref failure) is { } error) throw new InvalidOperationException(name + " connection failed.", error);
    }
    public void Dispose()
    {
        stop.Cancel();
        client.Dispose();
        try { receiveTask?.Wait(TimeSpan.FromSeconds(1)); } catch (AggregateException) { }
        stop.Dispose();
    }
}
