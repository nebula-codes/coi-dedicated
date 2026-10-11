using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

string repository = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(repository, "CoiDedicated.slnx"))) repository = Directory.GetParent(repository)?.FullName ?? throw new Exception("Run inside the repository.");
string managerDll = args.Length == 2 && args[0] == "--manager" ? Path.GetFullPath(args[1])
    : Path.Combine(repository, "src", "CoiDedicated.Manager", "bin", "Debug", "net10.0", "CoiDedicated.Manager.dll");
string scratch = Path.Combine(repository, ".work", "manager-http-" + Guid.NewGuid().ToString("N"));
string game = Path.Combine(scratch, "game"), data = Path.Combine(scratch, "data"), managerData = Path.Combine(data, ".manager");
Directory.CreateDirectory(Path.Combine(game, "Captain of Industry_Data", "Managed"));
Directory.CreateDirectory(Path.Combine(game, "Maps"));
foreach (string name in new[] { "Mafi.dll", "Mafi.Core.dll", "Mafi.Base.dll" }) File.WriteAllText(Path.Combine(game, "Captain of Industry_Data", "Managed", name), "HTTP fixture only; never executed");
File.WriteAllText(Path.Combine(game, "Maps", "New Haven.map"), "fixture");
var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
var origin = new Uri($"http://127.0.0.1:{port}");
var cookies = new CookieContainer();
using var browser = new HttpClient(new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false }) { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(10) };
using var guest = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }) { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(10) };
List<string> passed = [];
Process? manager = null;
string output = "", errors = "";
try
{
    await Start();
    Check((await guest.GetAsync("/api/health")).IsSuccessStatusCode, "Public health");
    Check((await guest.GetAsync("/")).StatusCode == HttpStatusCode.Redirect, "Dashboard redirects to login");
    Check((await guest.GetAsync("/login.html")).IsSuccessStatusCode, "Login page is public");
    Check((await guest.GetAsync("/api/state")).StatusCode == HttpStatusCode.Unauthorized, "Anonymous state denied");
    Check((await Send(guest, HttpMethod.Post, "/api/servers/00000000000000000000000000000000/actions/start", new { })).StatusCode == HttpStatusCode.Unauthorized, "Anonymous server action denied");
    foreach (string path in new[] { "/api/servers/00000000000000000000000000000000/join-key", "/api/servers/00000000000000000000000000000000/logs/download", "/api/servers/00000000000000000000000000000000/backups/nope/download" })
        Check((await guest.GetAsync(path)).StatusCode == HttpStatusCode.Unauthorized, "Anonymous private download denied: " + path.Split('/').Last());
    using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/health"))
    { request.Headers.Host = "attacker.example:" + port; Check((await guest.SendAsync(request)).StatusCode == HttpStatusCode.Forbidden, "Untrusted DNS Host denied"); }
    using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/health"))
    { request.Headers.Host = "nas.example:" + port; Check((await guest.SendAsync(request)).IsSuccessStatusCode, "Explicit DNS host allowed"); }
    using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/health"))
    { request.Headers.Add("Sec-Fetch-Site", "cross-site"); Check((await guest.SendAsync(request)).StatusCode == HttpStatusCode.Forbidden, "Cross-site resource denied"); }
    string keyPath = Path.Combine(managerData, "admin.key");
    string token = File.ReadAllText(keyPath).Trim();
    string keyHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(keyPath)));
    Check(token.Length == 64 && token.All(Uri.IsHexDigit), "Private administrator key generated");
    Check((await Send(guest, HttpMethod.Post, "/api/auth/login", new { token = new string('0', 64) })).StatusCode == HttpStatusCode.Unauthorized, "Incorrect key denied");
    Check((await Send(browser, HttpMethod.Post, "/api/auth/login", new { token }, "http://attacker.example")).StatusCode == HttpStatusCode.Forbidden, "Cross-origin login denied");
    using (var login = await Send(browser, HttpMethod.Post, "/api/auth/login", new { token }))
    {
        Check(login.IsSuccessStatusCode, "Administrator login");
        string cookie = string.Join(";", login.Headers.GetValues("Set-Cookie"));
        Check(cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) && cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase), "Cookie protects script/cross-site access");
        Check(!cookie.Contains(token, StringComparison.Ordinal), "Cookie does not expose access key");
    }
    using var state = JsonDocument.Parse(await browser.GetStringAsync("/api/state"));
    using var healthInfo = JsonDocument.Parse(await guest.GetStringAsync("/api/health"));
    string version = state.RootElement.GetProperty("version").GetString()!;
    Check(version == healthInfo.RootElement.GetProperty("version").GetString() && !version.Contains('+'), "Dashboard and health report the same release version");
    string id = state.RootElement.GetProperty("profiles")[0].GetProperty("id").GetString()!;
    Check(state.RootElement.GetProperty("profiles").GetArrayLength() == 1 && state.RootElement.GetProperty("environment").GetProperty("containerMode").GetBoolean(), "Single managed container profile");
    Check(state.RootElement.GetProperty("environment").GetProperty("publicGamePort").GetInt32() == 29551, "Published game port exposed");
    var input = new { name = "HTTP island", gameDirectory = "/untrusted/path", map = "New Haven", bindAddress = "127.0.0.1", controlPort = 30001, gamePort = 30002, autosaveSeconds = 90, startingFactory = false };
    Check((await Send(browser, HttpMethod.Post, "/api/servers", input)).StatusCode == HttpStatusCode.Conflict, "Container second profile denied");
    Check((await Send(browser, HttpMethod.Put, $"/api/servers/{id}", input, "http://attacker.example")).StatusCode == HttpStatusCode.Forbidden, "Cross-origin write denied");
    using (var request = new HttpRequestMessage(HttpMethod.Put, $"/api/servers/{id}") { Content = JsonContent.Create(input) })
        Check((await browser.SendAsync(request)).StatusCode == HttpStatusCode.Forbidden, "Write without dashboard header denied");
    using (var update = await Send(browser, HttpMethod.Put, $"/api/servers/{id}", input))
    {
        Check(update.IsSuccessStatusCode, "Stopped profile settings update");
        using var changed = JsonDocument.Parse(await update.Content.ReadAsStringAsync());
        Check(changed.RootElement.GetProperty("gamePort").GetInt32() == 28551 && changed.RootElement.GetProperty("controlPort").GetInt32() == 28550
            && changed.RootElement.GetProperty("bindAddress").GetString() == "0.0.0.0" && changed.RootElement.GetProperty("gameDirectory").GetString() == game, "Container paths and ports pinned");
    }
    byte[] playerKey = RandomNumberGenerator.GetBytes(32);
    await File.WriteAllBytesAsync(Path.Combine(data, "join.key"), playerKey);
    Check((await browser.GetByteArrayAsync($"/api/servers/{id}/join-key")).SequenceEqual(playerKey), "Authenticated join-key download");
    byte[] invalidSave = [1, 2, 3, 4];
    using (var import = new HttpRequestMessage(HttpMethod.Post, $"/api/servers/{id}/saves/import?name=HTTP") { Content = new ByteArrayContent(invalidSave) })
    {
        import.Headers.Add("X-Manager-Request", "1"); import.Headers.Add("Origin", origin.GetLeftPart(UriPartial.Authority));
        import.Content.Headers.ContentType = new("application/octet-stream");
        Check((await browser.SendAsync(import)).StatusCode == HttpStatusCode.BadRequest, "Authenticated invalid save still validated");
    }
    await Stop(); await Start();
    Check((await browser.GetAsync("/api/state")).IsSuccessStatusCode, "Session survives manager restart");
    Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(keyPath))) == keyHash, "Admin key persists across restart");
    Check((await Send(browser, HttpMethod.Post, "/api/auth/logout", new { })).IsSuccessStatusCode && (await browser.GetAsync("/api/state")).StatusCode == HttpStatusCode.Unauthorized, "Logout blocks later requests");
    Check(!output.Contains(token, StringComparison.Ordinal) && !errors.Contains(token, StringComparison.Ordinal), "Key absent from logs");
    await File.WriteAllTextAsync(Path.Combine(scratch, "results.json"), JsonSerializer.Serialize(new { version, passed }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"{passed.Count} manager HTTP checks passed. Evidence: {scratch}");
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
finally { await Stop(); await File.WriteAllTextAsync(Path.Combine(scratch, "manager.log"), output + errors); }

void Check(bool condition, string name) { if (!condition) throw new Exception("Failed: " + name); passed.Add(name); }
async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object value, string? requestOrigin = null)
{
    using var message = new HttpRequestMessage(method, path) { Content = JsonContent.Create(value) };
    message.Headers.Add("X-Manager-Request", "1"); message.Headers.Add("Origin", requestOrigin ?? origin.GetLeftPart(UriPartial.Authority));
    return await client.SendAsync(message);
}
async Task Start()
{
    var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = repository };
    foreach (string argument in new[] { managerDll, "--container", "--no-autostart", "--repository", repository, "--game-dir", game, "--server-data-dir", data, "--data-dir", managerData, "--host-dll", Path.Combine(scratch, "absent-host.dll"), "--listen", "127.0.0.1", "--port", port.ToString(), "--public-game-port", "29551", "--allowed-host", "nas.example" }) start.ArgumentList.Add(argument);
    manager = Process.Start(start)!;
    manager.OutputDataReceived += (_, e) => { if (e.Data is not null) output += e.Data + "\n"; };
    manager.ErrorDataReceived += (_, e) => { if (e.Data is not null) errors += e.Data + "\n"; };
    manager.BeginOutputReadLine(); manager.BeginErrorReadLine();
    for (int i = 0; i < 100; i++)
    {
        if (manager.HasExited) throw new Exception("Manager exited during startup. Inspect fixture logs.");
        try { if ((await guest.GetAsync("/api/health")).IsSuccessStatusCode) return; } catch (HttpRequestException) { }
        await Task.Delay(100);
    }
    throw new Exception("Manager did not start.");
}
async Task Stop()
{
    if (manager is null) return;
    if (!manager.HasExited) { manager.Kill(entireProcessTree: true); await manager.WaitForExitAsync(); }
    manager.Dispose(); manager = null;
}
