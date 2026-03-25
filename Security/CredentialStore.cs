using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ClaudeSW.Security
{
    public static class CredentialStore
    {
        private static readonly string StorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClaudeSW");

        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ClaudeSW-v2-provider-entropy-salt");

        public static void SaveApiKey(string providerKey, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(providerKey))
                throw new ArgumentException("Provider key cannot be empty.");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key cannot be empty.");

            Directory.CreateDirectory(StorePath);

            var encBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(apiKey),
                BuildEntropy(providerKey),
                DataProtectionScope.CurrentUser);

            File.WriteAllBytes(GetCredentialPath(providerKey), encBytes);
        }

        public static string LoadApiKey(string providerKey)
        {
            var path = GetCredentialPath(providerKey);
            if (!File.Exists(path))
                return null;

            var encBytes = File.ReadAllBytes(path);
            var plainBytes = ProtectedData.Unprotect(encBytes, BuildEntropy(providerKey), DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }

        public static bool HasApiKey(string providerKey)
        {
            return File.Exists(GetCredentialPath(providerKey));
        }

        public static void DeleteApiKey(string providerKey)
        {
            var path = GetCredentialPath(providerKey);
            if (File.Exists(path))
                File.Delete(path);
        }

        private static string GetCredentialPath(string providerKey)
        {
            return Path.Combine(StorePath, providerKey.ToLowerInvariant() + ".credentials.enc");
        }

        private static byte[] BuildEntropy(string providerKey)
        {
            return Combine(Entropy, Encoding.UTF8.GetBytes("|" + providerKey.ToLowerInvariant()));
        }

        private static byte[] Combine(byte[] left, byte[] right)
        {
            var combined = new byte[left.Length + right.Length];
            Buffer.BlockCopy(left, 0, combined, 0, left.Length);
            Buffer.BlockCopy(right, 0, combined, left.Length, right.Length);
            return combined;
        }
    }
}
