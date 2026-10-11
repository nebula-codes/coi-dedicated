using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoiDedicated.Manager;
using CoiDedicated.Manager.Core;

if (args is ["--health-check"])
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var response = await client.GetAsync("http://127.0.0.1:28560/api/health");
        using var health = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Environment.ExitCode = response.IsSuccessStatusCode && health.RootElement.GetProperty("status").GetString() == "ok"
            && health.RootElement.GetProperty("application").GetString() == "coi-dedicated-manager" ? 0 : 1;
    }
    catch { Environment.ExitCode = 1; }
    return;
}

ManagerOptions options = ManagerOptions.Parse(args);
string managerVersion = typeof(ManagerOptions).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.1.0";
Directory.CreateDirectory(options.DataRoot);
ManagerAuthentication? authentication = options.RequiresAuthentication ? new ManagerAuthentication(options.DataRoot) : null;
using var managerLease = new FileStream(Path.Combine(options.DataRoot, ".manager.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
string webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
if (!Directory.Exists(webRoot)) webRoot = Path.Combine(options.RepositoryRoot, "src", "CoiDedicated.Manager", "wwwroot");
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = Array.Empty<string>(), ContentRootPath = options.RepositoryRoot, WebRootPath = webRoot });
string listenHost = options.ListenAddress.Contains(':') ? $"[{options.ListenAddress}]" : options.ListenAddress;
builder.WebHost.UseUrls($"http://{listenHost}:{options.Port}");
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = 256L * 1024 * 1024);
builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.Configure<RouteHandlerOptions>(routing => routing.ThrowOnBadRequest = true);
if (options.ContainerMode) builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = TimeSpan.FromSeconds(270));
builder.Services.AddSingleton(options);
authentication?.Configure(builder.Services, options.DataRoot);
var profiles = new ProfileStore(options.DataRoot, options.ContainerMode
    ? new ContainerProfileConfiguration(options.GameDirectory!, options.ServerDataDirectory!, options.ServerName, options.Map, options.AutosaveSeconds, options.StartingFactory) : null);
profiles.Initialize();
builder.Services.AddSingleton(profiles);
builder.Services.AddSingleton<SaveLibrary>();
builder.Services.AddSingleton<ManagerService>();
builder.Services.AddHostedService(service => service.GetRequiredService<ManagerService>());
var app = builder.Build();
if (authentication is not null) app.UseAuthentication();

app.Use(async (context, next) =>
{
    if (!ManagerAuthentication.ValidHost(context, options))
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
            if (context.Request.Headers["X-Manager-Request"] != "1" || (origin.Length > 0 && !origin.Equals($"{context.Request.Scheme}://{context.Request.Host}", StringComparison.OrdinalIgnoreCase)))
            { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { error = "Use the manager dashboard on the same address for this action." }); return; }
        }
    }
    if (authentication is not null && !ManagerAuthentication.IsPublicPath(context.Request.Path) && context.User.Identity?.IsAuthenticated != true)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (context.Request.Path.StartsWithSegments("/api"))
        { context.Response.StatusCode = 401; await context.Response.WriteAsJsonAsync(new { error = "Sign in to the server manager to continue." }); }
        else context.Response.Redirect("/login.html");
        return;
    }
    try { await next(); }
    catch (Exception error) when (!context.Response.HasStarted)
    {
        int status = error switch
        {
            KeyNotFoundException or FileNotFoundException => 404,
            BadHttpRequestException bad => bad.StatusCode,
            ArgumentException or JsonException or InvalidDataException => 400,
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

if (authentication is not null)
{
    app.UseRateLimiter();
    authentication.MapEndpoints(app);
}
else app.MapGet("/api/auth/session", () => new { authenticationRequired = false, authenticated = true });
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = response => response.Context.Response.Headers.CacheControl = "no-cache" });
app.MapGet("/api/health", () => new { status = "ok", version = managerVersion, application = "coi-dedicated-manager" });
app.MapGet("/api/state", (ManagerService service, HttpRequest request) =>
{
    var all = profiles.List();
    string game = all.FirstOrDefault()?.GameDirectory ?? ProfileStore.GetDefaultGameDirectory();
    string[] maps;
    try { maps = profiles.GetMaps(game).ToArray(); } catch { maps = Array.Empty<string>(); }
    return new { profiles = all, servers = service.Snapshots(), version = managerVersion, environment = new { gameDirectory = game, maps,
        addresses = options.ContainerMode ? new[] { request.Host.Host } : LocalAddresses(), hostName = options.ContainerMode ? request.Host.Host : Environment.MachineName,
        hostBuildExists = File.Exists(options.HostDll), containerMode = options.ContainerMode, authenticationRequired = options.RequiresAuthentication, publicGamePort = options.PublicGamePort } };
});
app.MapGet("/api/maps", (string gameDirectory) => new { maps = profiles.GetMaps(options.ContainerMode ? options.GameDirectory! : gameDirectory) });
app.MapPost("/api/servers", async (ProfileInput input, ManagerService service) =>
{
    if (options.ContainerMode) throw new InvalidOperationException("This stack manages one island. Create another stack with its own data volume to host another server.");
    if (input.ControlPort == options.Port || input.GamePort == options.Port) throw new ArgumentException("Game and admin ports must differ from the manager port.");
    var profile = profiles.Create(input);
    await service.Get(profile.Id).InitializeAsync();
    return Results.Created($"/api/servers/{profile.Id}", profile);
});
app.MapPut("/api/servers/{id}", async (string id, ProfileInput input, ManagerService service) =>
{
    if (options.ContainerMode) input = input with { GameDirectory = options.GameDirectory!, BindAddress = "0.0.0.0", GamePort = 28551, ControlPort = 28550 };
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
