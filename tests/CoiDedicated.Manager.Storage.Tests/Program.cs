using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using CoiDedicated.Manager.Core;

var root = Path.Combine(Path.GetFullPath(".work"), "manager-storage-tests-" + Guid.NewGuid().ToString("N"));
var game = Path.Combine(root, "fake-installed-game");
Directory.CreateDirectory(Path.Combine(game, "Maps"));
Directory.CreateDirectory(Path.Combine(game, "Captain of Industry_Data", "Managed"));
foreach (string dll in new[] { "Mafi", "Mafi.Core", "Mafi.Base" })
    File.WriteAllBytes(Path.Combine(game, "Captain of Industry_Data", "Managed", dll + ".dll"), []);
File.WriteAllText(Path.Combine(game, "Maps", "New Haven.map"), "fixture");
File.WriteAllText(Path.Combine(game, "Maps", "Sandbox.map"), "fixture");
string? previousGame = Environment.GetEnvironmentVariable("COI_GAME_DIR");
Environment.SetEnvironmentVariable("COI_GAME_DIR", game);
int failures = 0;
var tests = new (string Name, Func<Task> Run)[]
{
    ("Default profile survives manager restart and derives its data path", Defaults),
    ("Invalid updates preserve committed configuration", Validation),
    ("Ports are exclusive across profiles", Ports),
    ("Configuration ignores a supplied data directory", DerivedPaths),
    ("Container migration adopts the existing island and keys without copying or replacing them", ContainerMigration),
    ("Container settings survive restart while deployment paths and ports remain fixed", ContainerSettings),
    ("Container run intent preserves explicit stop and start across recreation", RequestedState),
    ("Non-seekable upload is archived with exact SHA and name", Import),
    ("Bad headers and truncated saves never become backups", BadSaves),
    ("Oversized snapshot rejected without publishing archive", Oversized),
    ("Restore preserves previous island and immutable pre-restore backup", Restore),
    ("Tampered archive cannot replace current island", Tampered),
    ("Cancelled captures do not publish backups", Cancelled),
    ("Actual installed-game snapshot passes independent header validation", RealSave)
};
try
{
    foreach (var test in tests)
    {
        try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
        catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + ex); }
    }
}
finally { Environment.SetEnvironmentVariable("COI_GAME_DIR", previousGame); }
Console.WriteLine($"{tests.Length - failures}/{tests.Length} manager storage tests passed.");
return failures == 0 ? 0 : 1;

ProfileStore NewStore()
{
    var store = new ProfileStore(Path.Combine(root, Guid.NewGuid().ToString("N")));
    store.Initialize();
    return store;
}
ProfileInput Input(ServerProfile profile) => new(profile.Name, profile.GameDirectory, profile.Map, profile.BindAddress,
    profile.ControlPort, profile.GamePort, profile.AutosaveSeconds, profile.StartingFactory);

Task Defaults()
{
    var store = NewStore();
    var profile = store.List().Single();
    Equal("New Haven", profile.Name);
    Equal("127.0.0.1", profile.BindAddress);
    Equal(Path.Combine(store.RootDirectory, "servers", profile.Id), profile.DataDirectory);
    var loaded = new ProfileStore(store.RootDirectory);
    loaded.Initialize();
    Equal(profile, loaded.List().Single());
    Equal("New Haven,Sandbox", string.Join(',', loaded.GetMaps(game)));
    return Task.CompletedTask;
}

async Task Validation()
{
    var store = NewStore();
    var profile = store.List().Single();
    string original = File.ReadAllText(Path.Combine(store.RootDirectory, "profiles.json"));
    foreach (var bad in new[]
    {
        Input(profile) with { Name = "" }, Input(profile) with { Name = "bad\nname" },
        Input(profile) with { Map = "../New Haven" }, Input(profile) with { Map = "Unshipped" },
        Input(profile) with { BindAddress = "localhost" }, Input(profile) with { AutosaveSeconds = 9 },
        Input(profile) with { AutosaveSeconds = 3601 }, Input(profile) with { GameDirectory = "relative" },
        Input(profile) with { ControlPort = 0 }, Input(profile) with { ControlPort = profile.GamePort }
    }) await Throws<ArgumentException>(() => Task.FromResult(store.Update(profile.Id, bad)));
    Equal(original, File.ReadAllText(Path.Combine(store.RootDirectory, "profiles.json")));
    var updated = store.Update(profile.Id, Input(profile) with { Name = "  Harbor  ", BindAddress = "0.0.0.0" });
    Equal("Harbor", updated.Name);
    Equal(profile.CreatedUtc, updated.CreatedUtc);
    Equal("Harbor", new ProfileStore(store.RootDirectory).List().Single().Name);
    Equal(original, File.ReadAllText(Path.Combine(store.RootDirectory, "profiles.json.previous")));
}

async Task Ports()
{
    var store = NewStore();
    var profile = store.List().Single();
    await Throws<ArgumentException>(() => Task.FromResult(store.Create(Input(profile) with { Name = "Second", ControlPort = profile.GamePort, GamePort = 28553 })));
    var second = store.Create(Input(profile) with { Name = "Second", ControlPort = 28552, GamePort = 28553 });
    await Throws<ArgumentException>(() => Task.FromResult(store.Update(profile.Id, Input(profile) with { GamePort = second.ControlPort })));
    Equal(2, store.List().Count);
}

Task DerivedPaths()
{
    var store = NewStore();
    var profile = store.List().Single();
    File.WriteAllText(Path.Combine(store.RootDirectory, "profiles.json"), JsonSerializer.Serialize(new[] { profile with { DataDirectory = "C:\\arbitrary" } }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    var loaded = new ProfileStore(store.RootDirectory);
    Equal(profile.DataDirectory, loaded.List().Single().DataDirectory);
    return Task.CompletedTask;
}

async Task ContainerMigration()
{
    string data = Path.Combine(root, "existing-container-volume");
    Directory.CreateDirectory(data);
    byte[] original = Save(19);
    await File.WriteAllBytesAsync(Path.Combine(data, "island.save"), original);
    await File.WriteAllTextAsync(Path.Combine(data, "join.key"), "private-test-join-key");
    await File.WriteAllTextAsync(Path.Combine(data, "control.key"), "private-test-control-key");
    var store = new ProfileStore(Path.Combine(data, ".manager"), new(game, data));
    var profile = store.List().Single();
    Equal(data, profile.DataDirectory);
    Equal("0.0.0.0", profile.BindAddress);
    Equal(28550, profile.ControlPort);
    Equal(28551, profile.GamePort);
    var library = new SaveLibrary(store);
    var backup = await library.BackupCurrentAsync(profile.Id, "Migrated island");
    Equal(Convert.ToHexString(original), Convert.ToHexString(await File.ReadAllBytesAsync(Path.Combine(data, "island.save"))));
    Equal("private-test-join-key", await File.ReadAllTextAsync(Path.Combine(data, "join.key")));
    Equal("private-test-control-key", await File.ReadAllTextAsync(Path.Combine(data, "control.key")));
    Equal(Path.Combine(data, ".manager", "backups", profile.Id, backup.Id + ".save"), library.GetBackupPath(profile.Id, backup.Id));
    Equal(false, Directory.Exists(Path.Combine(data, ".manager", "servers")));
    await Throws<InvalidOperationException>(() => Task.FromResult(store.Create(Input(profile))));
}

Task ContainerSettings()
{
    string data = Path.Combine(root, "container-settings");
    var store = new ProfileStore(Path.Combine(data, ".manager"), new(game, data));
    var profile = store.List().Single();
    var changed = store.Update(profile.Id, Input(profile) with { Name = "Harbor", Map = "Sandbox", AutosaveSeconds = 120,
        StartingFactory = false, GameDirectory = "untrusted-request-path", BindAddress = "127.0.0.1", ControlPort = 33333, GamePort = 33334 });
    Equal(game, changed.GameDirectory);
    Equal("0.0.0.0", changed.BindAddress);
    Equal(28550, changed.ControlPort);
    string nextMount = Path.Combine(root, "replacement-game-view");
    var reloaded = new ProfileStore(store.RootDirectory, new(nextMount, data, "Environment default", "New Haven", 60, true)).List().Single();
    Equal(changed with { GameDirectory = nextMount }, reloaded);
    return Task.CompletedTask;
}

async Task RequestedState()
{
    string directory = Path.Combine(root, "run-intent");
    string id = Guid.NewGuid().ToString("N");
    var state = new RequestedRunStateStore(directory);
    Equal(true, state.ShouldRun(id));
    state.Set(id, false);
    Equal(false, new RequestedRunStateStore(directory).ShouldRun(id));
    state.Set(id, true);
    Equal(true, new RequestedRunStateStore(directory, defaultRunning: false).ShouldRun(id));
    await File.WriteAllTextAsync(Path.Combine(directory, "requested-run-state.json"), "corrupt");
    await Throws<JsonException>(() => Task.FromResult(new RequestedRunStateStore(directory).ShouldRun(id)));
}

async Task Import()
{
    var store = NewStore();
    var library = new SaveLibrary(store);
    var profile = store.List().Single();
    byte[] content = Save(7);
    using var input = new FragmentedStream(content);
    var backup = await library.ImportAsync(profile.Id, "Island <v1>", input);
    Equal((long)content.Length, backup.Bytes);
    Equal(Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(), backup.Sha256);
    Equal("Island <v1>", backup.Name);
    Equal(backup, library.List(profile.Id).Single());
    Equal(Convert.ToHexString(content), Convert.ToHexString(File.ReadAllBytes(library.GetBackupPath(profile.Id, backup.Id))));
    await Throws<ArgumentException>(() => Task.FromResult(library.GetBackupPath(profile.Id, "../../outside")));
}

async Task BadSaves()
{
    var store = NewStore();
    var profile = store.List().Single();
    var library = new SaveLibrary(store);
    byte[] wrongSize = Save(7); wrongSize[16] = 12;
    foreach (var data in new[] { Array.Empty<byte>(), new byte[100], wrongSize, Save(7)[..^1] })
        await Throws<InvalidDataException>(() => library.ImportAsync(profile.Id, "Bad", new MemoryStream(data)));
    Equal(0, library.List(profile.Id).Count);
    Equal(0, Directory.GetFiles(Path.Combine(store.RootDirectory, "backups", profile.Id), "*.partial").Length);
}

async Task Oversized()
{
    var store = NewStore();
    var profile = store.List().Single();
    var library = new SaveLibrary(store);
    await Throws<InvalidDataException>(() => library.CaptureAsync(profile.Id, "Too large", (path, _) =>
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        file.SetLength(SaveLibrary.MaximumSaveBytes + 1);
        return Task.CompletedTask;
    }));
    Equal(0, library.List(profile.Id).Count);
}

async Task Restore()
{
    var store = NewStore();
    var profile = store.List().Single();
    var library = new SaveLibrary(store);
    byte[] old = Save(7), replacement = Save(31);
    var backup = await library.ImportAsync(profile.Id, "Restore me", new MemoryStream(replacement));
    Directory.CreateDirectory(profile.DataDirectory);
    string current = Path.Combine(profile.DataDirectory, "island.save");
    File.WriteAllBytes(current, old);
    var before = await library.RestoreAsync(profile.Id, backup.Id) ?? throw new Exception("Missing pre-restore backup.");
    Equal(Convert.ToHexString(replacement), Convert.ToHexString(File.ReadAllBytes(current)));
    Equal(Convert.ToHexString(old), Convert.ToHexString(File.ReadAllBytes(current + ".previous")));
    Equal(Convert.ToHexString(old), Convert.ToHexString(File.ReadAllBytes(library.GetBackupPath(profile.Id, before.Id))));
    Equal(2, library.List(profile.Id).Count);
}

async Task Tampered()
{
    var store = NewStore();
    var profile = store.List().Single();
    var library = new SaveLibrary(store);
    var backup = await library.ImportAsync(profile.Id, "Original", new MemoryStream(Save(7)));
    File.WriteAllBytes(library.GetBackupPath(profile.Id, backup.Id), Save(31));
    Directory.CreateDirectory(profile.DataDirectory);
    var old = Save(66);
    string current = Path.Combine(profile.DataDirectory, "island.save");
    File.WriteAllBytes(current, old);
    await Throws<InvalidDataException>(() => library.RestoreAsync(profile.Id, backup.Id));
    Equal(Convert.ToHexString(old), Convert.ToHexString(File.ReadAllBytes(current)));
    Equal(1, library.List(profile.Id).Count);
}

async Task Cancelled()
{
    var store = NewStore();
    var profile = store.List().Single();
    var library = new SaveLibrary(store);
    using var cancelled = new CancellationTokenSource();
    await Throws<OperationCanceledException>(() => library.CaptureAsync(profile.Id, "Cancelled", async (path, token) =>
    {
        await File.WriteAllBytesAsync(path, Save(7), token);
        cancelled.Cancel();
    }, cancelled.Token));
    Equal(0, library.List(profile.Id).Count);
}

async Task RealSave()
{
    string actual = Path.GetFullPath(".work/downloaded-island.save");
    if (!File.Exists(actual)) { Console.WriteLine("  SKIP: no local integration snapshot available"); return; }
    var store = NewStore();
    var library = new SaveLibrary(store);
    await using var input = File.OpenRead(actual);
    var backup = await library.ImportAsync(store.List().Single().Id, "Actual CoI snapshot", input);
    Equal(input.Length, backup.Bytes);
}

static byte[] Save(byte value)
{
    var content = new byte[72];
    "evaSiFaM"u8.CopyTo(content);
    BinaryPrimitives.WriteInt32LittleEndian(content.AsSpan(8), 333);
    BinaryPrimitives.WriteInt32LittleEndian(content.AsSpan(12), 0);
    BinaryPrimitives.WriteInt64LittleEndian(content.AsSpan(16), content.Length - 40);
    content.AsSpan(40).Fill(value);
    return content;
}
static void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception($"Expected {expected}, received {actual}."); }
static async Task Throws<T>(Func<Task> operation) where T : Exception
{
    try { await operation(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}
sealed class FragmentedStream(byte[] content) : MemoryStream(content)
{
    public override bool CanSeek => false;
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => base.ReadAsync(buffer[..Math.Min(buffer.Length, 3)], cancellationToken);
}
