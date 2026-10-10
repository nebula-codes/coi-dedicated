using System;
using System.Security.Cryptography;
using System.Text;

namespace CoiDedicated.Protocol
{
    /// <summary>Challenge-response proof for the dedicated host control connection.</summary>
    public static class ControlAuthentication
    {
        public const int NonceLength = 32;
        public const int ProofLength = 32;
        public const int MaxClientNameByteLength = 64;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private const string Domain = "coi-dedicated-v1\n";

        /// <summary>HMAC-SHA256(key=UTF8(secret), data=nonce || UTF8(domain + clientName)).</summary>
        public static byte[] ComputeProof(string secret, ReadOnlySpan<byte> nonce, string clientName)
        {
            if (string.IsNullOrEmpty(secret)) throw new ArgumentException("A nonempty control secret is required.", nameof(secret));
            if (clientName == null) throw new ArgumentNullException(nameof(clientName));
            if (nonce.Length != NonceLength) throw new ArgumentException("Control nonce must be 32 bytes.", nameof(nonce));
            if (Utf8.GetByteCount(clientName) > MaxClientNameByteLength)
                throw new ArgumentOutOfRangeException(nameof(clientName), "Client name exceeds 64 UTF-8 bytes.");

            byte[] suffix = Utf8.GetBytes(Domain + clientName);
            var data = new byte[NonceLength + suffix.Length];
            nonce.CopyTo(data);
            suffix.CopyTo(data, NonceLength);
            byte[] key = Utf8.GetBytes(secret);
            try
            {
                using var hmac = new HMACSHA256(key);
                return hmac.ComputeHash(data);
            }
            finally { CryptographicOperations.ZeroMemory(key); }
        }

        public static bool VerifyProof(string secret, ReadOnlySpan<byte> nonce, string clientName, ReadOnlySpan<byte> proof)
        {
            if (proof.Length != ProofLength) return false;
            byte[] expected = ComputeProof(secret, nonce, clientName);
            try { return CryptographicOperations.FixedTimeEquals(expected, proof); }
            finally { CryptographicOperations.ZeroMemory(expected); }
        }
    }
}
