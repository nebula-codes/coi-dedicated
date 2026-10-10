using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CoiDedicated.GameAdapter
{
    /// <summary>Exact simulation-binary identity shared by the host and game client.</summary>
    public static class GameBuildIdentity
    {
        public static string Compute(string managedDirectory)
        {
            if (string.IsNullOrWhiteSpace(managedDirectory)) throw new ArgumentException("Game managed directory is required.", nameof(managedDirectory));
            // Fixed names and ordering, with a name terminator and a fixed-length
            // file digest, make the combined digest independent of installation paths.
            using var combined = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var sha = SHA256.Create();
            foreach (string name in new[] { "Mafi.dll", "Mafi.Core.dll", "Mafi.Base.dll" })
            {
                using var file = File.OpenRead(Path.Combine(managedDirectory, name));
                combined.AppendData(Encoding.UTF8.GetBytes(name + "\0"));
                combined.AppendData(sha.ComputeHash(file));
            }
            string digest = BitConverter.ToString(combined.GetHashAndReset()).Replace("-", "").ToLowerInvariant();
            // Bump the prototype revision when accepted command semantics change,
            // even if their field layout and game DLLs remain the same.
            return "CoI 0.8.7d / dedicated-prototype-5 / cmd" + GameCommandCodec.Version + " / " + digest;
        }
    }
}
