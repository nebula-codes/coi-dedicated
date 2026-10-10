using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoiDedicated.Manager;
using CoiDedicated.Manager.Core;

ManagerOptions options = ManagerOptions.Parse(args);
Directory.CreateDirectory(options.DataRoot);
using var managerLease = new FileStream(Path.Combine(options.DataRoot, ".manager.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
string webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
if (!Directory.Exists(webRoot)) webRoot = Path.Combine(options.RepositoryRoot, "src", "CoiDedicated.Manager", "wwwroot");
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = Array.Empty<string>(), ContentRootPath = options.RepositoryRoot, WebRootPath = webRoot });
builder.WebHost.UseUrls($"http://127.0.0.1:{options.Port}");
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = 256L * 1024 * 1024);
builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.Configure<RouteHandlerOptions>(routing => routing.ThrowOnBadRequest = true);
builder.Services.AddSingleton(options);
var profiles = new ProfileStore(options.DataRoot);
profiles.Initialize();
builder.Services.AddSingleton(profiles);
builder.Services.AddSingleton<SaveLibrary>();
builder.Services.AddSingleton<ManagerService>();
builder.Services.AddHostedService(service => service.GetRequiredService<ManagerService>());
var app = builder.Build();

app.Use(async (context, next) =>
{
    if (context.Request.Host.Port != options.Port || context.Request.Host.Host is not ("127.0.0.1" or "localhost")
        || context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip))
    { context.Response.StatusCode = 403; return; }
    string site = context.Request.Headers["Sec-Fetch-Site"].ToString();
    if (site == "cross-site") { context.Response.StatusCode = 403; return; }
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store";
        if (context.Request.Method is not ("GET" or "HEAD"))
        {
            string origin = context.Request.Headers.Origin.ToString();
            if (context.Request.Headers["X-Manager-Request"] != "1" || (origin.Length > 0 && origin != $"http://{context.Request.Host}"))
            { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { error = "Use the local manager dashboard for this action." }); return; }
        }
    }
    try { await next(); }
    catch (Exception error) when (!context.Response.HasStarted)
    {
        int status = error switch
        {
            KeyNotFoundException or FileNotFoundException => 404,
            BadHttpRequestException bad => bad.StatusCode,
            ArgumentException or JsonException => 400,
            UnauthorizedAccessException => 403,
            InvalidOperationException or IOException => 409,
            TimeoutException or OperationCanceledException or SocketException => 503,
            _ => 500
        };
        if (status == 500) app.Logger.LogError(error, "Manager operation failed");
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { error = status == 500 ? "The operation failed. Check the manager log for details." : error.Message });
    }
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = response => response.Context.Response.Headers.CacheControl = "no-cache" });
app.MapGet("/api/health", () => new { status = "ok", version = "0.1.0", application = "coi-dedicated-manager" });
app.MapGet("/api/state", (ManagerService service) =>
{
    var all = profiles.List();
    string game = all.FirstOrDefault()?.GameDirectory ?? ProfileStore.GetDefaultGameDirectory();
    string[] maps;
    try { maps = profiles.GetMaps(game).ToArray(); } catch { maps = Array.Empty<string>(); }
    return new { profiles = all, servers = service.Snapshots(), version = "0.1.0", environment = new { gameDirectory = game, maps, addresses = LocalAddresses(), hostName = Environment.MachineName, hostBuildExists = File.Exists(options.HostDll) } };
});
app.MapGet("/api/maps", (string gameDirectory) => new { maps = profiles.GetMaps(gameDirectory) });
app.MapPost("/api/servers", async (ProfileInput input, ManagerService service) =>
{
    if (input.ControlPort == options.Port || input.GamePort == options.Port) throw new ArgumentException("Game and admin ports must differ from the manager port.");
    var profile = profiles.Create(input);
    await service.Get(profile.Id).InitializeAsync();
    return Results.Created($"/api/servers/{profile.Id}", profile);
});
app.MapPut("/api/servers/{id}", async (string id, ProfileInput input, ManagerService service) =>
{
    if (input.ControlPort == options.Port || input.GamePort == options.Port) throw new ArgumentException("Game and admin ports must differ from the manager port.");
    return await service.Update(id, input);
});
app.MapGet("/api/servers/{id}", (string id, ManagerService service) => service.Get(id).Snapshot);
app.MapPost("/api/servers/{id}/actions/{action}", (string id, string action, ManagerService service) => service.Action(id, action));
app.MapGet("/api/servers/{id}/logs", (string id, long? after, ManagerService service) =>
{
    var server = service.Get(id);
    var entries = server.GetLogs(after ?? 0, 1000);
    return new { entries, next = entries.LastOrDefault()?.Sequence ?? after ?? 0, streamId = server.LogStreamId };
});
app.MapGet("/api/servers/{id}/logs/download", (string id, ManagerService service) =>
{
    _ = service.Get(id);
    string path = Path.Combine(options.DataRoot, "runtime", id, "server.log");
    if (!File.Exists(path)) throw new FileNotFoundException("No server log yet. Start the server first.");
    return Results.File(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), "text/plain", "server.log");
});
app.MapGet("/api/servers/{id}/saves", (string id, SaveLibrary saves) =>
{
    var current = new FileInfo(Path.Combine(profiles.Get(id).DataDirectory, "island.save"));
    return new { current = new { exists = current.Exists, bytes = current.Exists ? current.Length : 0, modifiedUtc = current.Exists ? (DateTimeOffset?)current.LastWriteTimeUtc : null }, backups = saves.List(id) };
});
app.MapPost("/api/servers/{id}/backups", (string id, BackupRequest input, ManagerService service) => service.Backup(id, input.Name));
app.MapPost("/api/servers/{id}/backups/{backupId}/restore", (string id, string backupId, ManagerService service) => service.Restore(id, backupId));
app.MapPost("/api/servers/{id}/saves/import", (string id, string? name, HttpRequest request, ManagerService service) => service.Import(id, name ?? "Imported island", request.Body));
app.MapGet("/api/servers/{id}/backups/{backupId}/download", (string id, string backupId, SaveLibrary saves) =>
    Results.File(saves.GetBackupPath(id, backupId), "application/octet-stream", "island-backup.save"));
app.MapGet("/api/servers/{id}/join-key", (string id) =>
{
    string path = Path.Combine(profiles.Get(id).DataDirectory, "join.key");
    if (!File.Exists(path)) throw new FileNotFoundException("Start the server once to generate its player key.");
    return Results.File(path, "application/octet-stream", "join.key");
});
if (options.OpenBrowser)
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try { Process.Start(new ProcessStartInfo($"http://127.0.0.1:{options.Port}") { UseShellExecute = true }); }
        catch (Exception error) { app.Logger.LogWarning(error, "Open http://127.0.0.1:{Port} to use the manager", options.Port); }
    });
await app.RunAsync();

static string[] LocalAddresses() => NetworkInterface.GetAllNetworkInterfaces()
    .Where(network => network.OperationalStatus == OperationalStatus.Up && network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
    .SelectMany(network => network.GetIPProperties().UnicastAddresses)
    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork && !address.Address.ToString().StartsWith("169.254."))
    .Select(address => address.Address.ToString()).Distinct().Order().ToArray();

internal sealed record BackupRequest(string? Name);
