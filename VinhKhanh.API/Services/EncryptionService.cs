using System;
using System.Security.Cryptography;
using System.Text;

namespace VinhKhanh.API.Services
{
    // Simple symmetric protection for PII using AES-GCM (POC). In production use proper key management (Azure Key Vault).
    public static class EncryptionService
    {
        // IMPORTANT: use secure key storage in production. Here we derive a key from config seed for POC.
        private static readonly byte[] _key = SHA256.HashData(Encoding.UTF8.GetBytes("change-this-secret-key"));

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return string.Empty;
            try
            {
                using var aes = new AesGcm(_key);
                var plaintext = Encoding.UTF8.GetBytes(plain);
                var nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
                RandomNumberGenerator.Fill(nonce);
                var cipher = new byte[plaintext.Length];
                var tag = new byte[AesGcm.TagByteSizes.MaxSize];
                aes.Encrypt(nonce, plaintext, cipher, tag);
                var outBytes = new byte[nonce.Length + tag.Length + cipher.Length];
                Buffer.BlockCopy(nonce, 0, outBytes, 0, nonce.Length);
                Buffer.BlockCopy(tag, 0, outBytes, nonce.Length, tag.Length);
                Buffer.BlockCopy(cipher, 0, outBytes, nonce.Length + tag.Length, cipher.Length);
                return Convert.ToBase64String(outBytes);
            }
            catch { return string.Empty; }
        }

        public static string Unprotect(string encrypted)
        {
            if (string.IsNullOrEmpty(encrypted)) return string.Empty;
            try
            {
                var all = Convert.FromBase64String(encrypted);
                var nonceLen = AesGcm.NonceByteSizes.MaxSize;
                var tagLen = AesGcm.TagByteSizes.MaxSize;
                var nonce = new byte[nonceLen];
                var tag = new byte[tagLen];
                var cipher = new byte[all.Length - nonceLen - tagLen];
                Buffer.BlockCopy(all, 0, nonce, 0, nonceLen);
                Buffer.BlockCopy(all, nonceLen, tag, 0, tagLen);
                Buffer.BlockCopy(all, nonceLen + tagLen, cipher, 0, cipher.Length);
                using var aes = new AesGcm(_key);
                var plain = new byte[cipher.Length];
                aes.Decrypt(nonce, cipher, tag, plain);
                return Encoding.UTF8.GetString(plain);
            }
            catch { return string.Empty; }
        }
    }
}
