using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace CoiDedicated.Manager.Core;

/// <summary>Immutable, size-bounded save archives. No game objects are deserialized here.</summary>
public sealed class SaveLibrary
{
    public const long MaximumSaveBytes = 256L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ProfileStore _profiles;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public SaveLibrary(ProfileStore profiles) => _profiles = profiles;

    public IReadOnlyList<SaveBackup> List(string profileId)
    {
        string directory = GetDirectory(profileId);
        if (!Directory.Exists(directory)) return [];
        return Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(ReadMetadata).Where(b => b.ProfileId == profileId && File.Exists(Path.Combine(directory, b.Id + ".save")))
            .OrderByDescending(b => b.CreatedUtc).ToArray();
    }

    public string GetBackupPath(string profileId, string backupId)
    {
        ProfileStore.ValidateId(backupId);
        string directory = GetDirectory(profileId);
        string path = Path.Combine(directory, backupId + ".save");
        var metadata = ReadMetadata(Path.Combine(directory, backupId + ".json"));
        if (metadata.ProfileId != profileId || metadata.Id != backupId || !File.Exists(path))
            throw new KeyNotFoundException("Save backup was not found.");
        return path;
    }

    public async Task<SaveBackup> ImportAsync(string profileId, string name, Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return await CaptureAsync(profileId, name, async (path, token) =>
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
            var buffer = new byte[65536];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
            {
                total += read;
                if (total > MaximumSaveBytes) throw new InvalidDataException("Save files must not exceed 256 MiB.");
                await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            }
            await output.FlushAsync(token).ConfigureAwait(false);
            output.Flush(true);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SaveBackup> BackupCurrentAsync(string profileId, string? name = null, CancellationToken cancellationToken = default)
    {
        string path = Path.Combine(_profiles.GetDataDirectory(profileId), "island.save");
        if (!File.Exists(path)) throw new InvalidOperationException("This server has no saved island yet.");
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        return await ImportAsync(profileId, name ?? $"Backup {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}", source, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SaveBackup> CaptureAsync(string profileId, string name, Func<string, CancellationToken, Task> writeSnapshot,
        CancellationToken cancellationToken = default)
    {
        string directory = GetDirectory(profileId);
        name = NormalizeName(name);
        var gate = _locks.GetOrAdd(profileId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string id = Guid.NewGuid().ToString("N");
        string temporary = Path.Combine(directory, id + ".partial");
        try
        {
            Directory.CreateDirectory(directory);
            await writeSnapshot(temporary, cancellationToken).ConfigureAwait(false);
            var (length, hash) = await InspectAsync(temporary, cancellationToken).ConfigureAwait(false);
            var metadata = new SaveBackup(id, profileId, name, length, hash, DateTimeOffset.UtcNow);
            File.Move(temporary, Path.Combine(directory, id + ".save"));
            WriteMetadata(Path.Combine(directory, id + ".json"), metadata);
            return metadata;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            gate.Release();
        }
    }

    /// <summary>The caller must serialize profile operations and ensure the server is stopped.</summary>
    public async Task<SaveBackup?> RestoreAsync(string profileId, string backupId, CancellationToken cancellationToken = default)
    {
        string source = GetBackupPath(profileId, backupId);
        var expected = ReadMetadata(Path.ChangeExtension(source, ".json"));
        var (length, hash) = await InspectAsync(source, cancellationToken).ConfigureAwait(false);
        if (length != expected.Bytes || !string.Equals(hash, expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The saved backup no longer matches its recorded SHA-256 checksum.");
        string data = _profiles.GetDataDirectory(profileId);
        Directory.CreateDirectory(data);
        string destination = Path.Combine(data, "island.save");
        SaveBackup? previous = null;
        if (File.Exists(destination)) previous = await BackupCurrentAsync(profileId, "Before restore " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"), cancellationToken).ConfigureAwait(false);
        string temporary = Path.Combine(data, ".restore-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(true);
            }
            // Recheck the staged file before committing: archives are never trusted solely by filename.
            var staged = await InspectAsync(temporary, cancellationToken).ConfigureAwait(false);
            if (staged.Bytes != expected.Bytes || !string.Equals(staged.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The backup changed while the restore was being prepared.");
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(destination)) File.Replace(temporary, destination, destination + ".previous");
            else File.Move(temporary, destination);
            return previous;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private string GetDirectory(string profileId)
    {
        _profiles.Get(profileId);
        return Path.Combine(_profiles.RootDirectory, "backups", profileId);
    }

    private static string NormalizeName(string name)
    {
        name = name?.Trim() ?? "";
        if (name.Length is < 1 or > 128 || name.Any(char.IsControl)) throw new ArgumentException("Backup name must contain 1–128 printable characters.");
        return name;
    }

    private static SaveBackup ReadMetadata(string path)
    {
        if (!File.Exists(path)) throw new KeyNotFoundException("Save backup metadata was not found.");
        var result = JsonSerializer.Deserialize<SaveBackup>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Save backup metadata is invalid.");
        ProfileStore.ValidateId(result.Id);
        ProfileStore.ValidateId(result.ProfileId);
        return result;
    }

    private static void WriteMetadata(string path, SaveBackup metadata)
    {
        string temporary = path + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, metadata, JsonOptions);
                output.Flush(true);
            }
            File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<(long Bytes, string Sha256)> InspectAsync(string path, CancellationToken token)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (input.Length < 17 || input.Length > MaximumSaveBytes) throw new InvalidDataException("Save files must contain a CoI header and not exceed 256 MiB.");
        byte[] header = new byte[Math.Min(40, input.Length)];
        await input.ReadExactlyAsync(header, token).ConfigureAwait(false);
        if (!header.AsSpan(0, 8).SequenceEqual("evaSiFaM"u8)) throw new InvalidDataException("This file does not have a Captain of Industry save header.");
        int version = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8, 4));
        int compression = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12, 4));
        if (version < 1 || compression is < 0 or > 1) throw new InvalidDataException("The save header has an invalid version or compression method.");
        // CoI v167+ includes a 40-byte header with the compressed payload length.
        if (version >= 167 && (header.Length < 40 || BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(16, 8)) != input.Length - 40))
            throw new InvalidDataException("The save file is truncated or its header length is invalid.");
        input.Position = 0;
        var hash = await SHA256.HashDataAsync(input, token).ConfigureAwait(false);
        return (input.Length, Convert.ToHexString(hash).ToLowerInvariant());
    }
}
