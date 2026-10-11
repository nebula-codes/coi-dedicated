using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;

namespace CoiDedicated.Manager;

internal sealed class ManagerAuthentication
{
    private const string Scheme = "ManagerAdmin";
    private readonly byte[] tokenHash;
    private readonly string identity;

    public ManagerAuthentication(string dataRoot)
    {
        MakePrivateDirectory(dataRoot);
        string path = Path.Combine(dataRoot, "admin.key");
        if (!File.Exists(path))
        {
            var settings = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) settings.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var file = new FileStream(path, settings);
            file.Write(Encoding.ASCII.GetBytes(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant() + "\n"));
            file.Flush(true);
        }
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        string token = File.ReadAllText(path).Trim();
        if (token.Length != 64 || !token.All(Uri.IsHexDigit)) throw new InvalidDataException("Manager admin.key must contain its 64-character generated access key. Restore the file or remove it while the manager is stopped to generate a new key.");
        tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        identity = Convert.ToHexString(tokenHash);
    }

    public void Configure(IServiceCollection services, string dataRoot)
    {
        string keyRing = Path.Combine(dataRoot, "session-keys");
        MakePrivateDirectory(keyRing);
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyRing)).SetApplicationName("CoI Dedicated Manager");
        services.AddAuthentication(Scheme).AddCookie(Scheme, options =>
        {
            options.Cookie.Name = "coi-manager-session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromHours(12);
            options.SlidingExpiration = true;
            options.Events.OnValidatePrincipal = context =>
            {
                if (context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) != identity) context.RejectPrincipal();
                return Task.CompletedTask;
            };
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.AddFixedWindowLimiter("manager-login", limiter =>
            {
                limiter.PermitLimit = 20;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });
        });
    }

    public void MapEndpoints(WebApplication app)
    {
        app.MapGet("/api/auth/session", (HttpContext context) => new { authenticationRequired = true, authenticated = context.User.Identity?.IsAuthenticated == true });
        app.MapPost("/api/auth/login", async (HttpContext context) =>
        {
            if (context.Request.ContentLength is > 4096) return Results.BadRequest(new { error = "Enter the generated manager access key." });
            var size = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (size is { IsReadOnly: false }) size.MaxRequestBodySize = 4096;
            LoginRequest? input;
            try { input = await context.Request.ReadFromJsonAsync<LoginRequest>(context.RequestAborted); }
            catch (System.Text.Json.JsonException) { return Results.BadRequest(new { error = "Enter the generated manager access key." }); }
            string token = input?.Token?.Trim() ?? "";
            if (token.Length != 64 || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(token)), tokenHash))
                return Results.Json(new { error = "That access key is incorrect. Copy the complete key from admin.key and try again." }, statusCode: 401);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, identity)], Scheme));
            await context.SignInAsync(Scheme, principal, new AuthenticationProperties { IsPersistent = true });
            return Results.Ok(new { message = "Signed in." });
        }).RequireRateLimiting("manager-login");
        app.MapPost("/api/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(Scheme);
            return Results.Ok(new { message = "Signed out." });
        });
    }

    public static bool IsPublicPath(PathString path) => path == "/api/health" || path == "/api/auth/session" || path == "/api/auth/login"
        || path == "/login.html" || path == "/login.js" || path == "/app.css" || path == "/favicon.ico";

    public static bool ValidHost(HttpContext context, ManagerOptions options)
    {
        string host = context.Request.Host.Host.Trim('[', ']');
        if (!options.RequiresAuthentication)
            return context.Request.Host.Port == options.Port && (host is "127.0.0.1" or "localhost" or "::1")
                && context.Connection.RemoteIpAddress is { } remote && IPAddress.IsLoopback(remote);
        // Literal IPs work with Docker's published address. DNS names are opt-in so an
        // arbitrary attacker's hostname cannot rebind to this dashboard on the LAN.
        return IPAddress.TryParse(host, out var address) && !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any)
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || (options.AllowedHosts ?? []).Contains(host, StringComparer.OrdinalIgnoreCase);
    }

    private static void MakePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private sealed record LoginRequest(string? Token);
}
