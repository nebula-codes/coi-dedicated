using System.Security.Cryptography;

namespace CoiDedicated;

/// <summary>Admits only game builds whose dedicated startup behavior was reviewed.</summary>
internal static class ServerGameCompatibility
{
    internal const string SupportedVersion = "0.8.7d";
    private static readonly (string Name, string Sha256)[] SupportedAssemblies =
    {
        ("Mafi.dll", "0adf871a75600bc21859b49d09943d087b10e3fd7a108c90a3381c5ae7f8da26"),
        ("Mafi.Core.dll", "6292fe37a4132a60b2dadae16d65e76af2a80b5ed251e10a75ad19676c8f187c"),
        ("Mafi.Base.dll", "6584e0c734170564d1d2632f64a2c25a782da2c16fe2888e131fe885f0a01bea")
    };

    internal static void Validate(string gameDirectory)
    {
        string managed = Path.Combine(gameDirectory, "Captain of Industry_Data", "Managed");
        foreach (var (name, expected) in SupportedAssemblies)
        {
            string path = Path.Combine(managed, name);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Server-only startup requires the complete supported CoI {SupportedVersion} installation. Missing {name}.", path);
            ValidateFile(path, name, expected);
        }
    }

    internal static void ValidateLoadedAssembly(System.Reflection.Assembly assembly)
    {
        string name = assembly.GetName().Name + ".dll";
        var supported = SupportedAssemblies.SingleOrDefault(item => item.Name == name);
        if (supported.Name is null || assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
            throw new NotSupportedException($"Cannot verify the loaded game assembly {name}; no island was loaded.");
        ValidateFile(assembly.Location, name, supported.Sha256);
    }

    private static void ValidateFile(string path, string name, string expected)
    {
        using var input = File.OpenRead(path);
        string actual = Convert.ToHexStringLower(SHA256.HashData(input));
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new NotSupportedException($"Unsupported CoI game build: {name} SHA-256 is {actual}. " +
                $"This server supports the reviewed {SupportedVersion} files only. No island was loaded or changed. " +
                "Install a matching server update; automatic patching of unknown game updates is disabled.");
    }
}
