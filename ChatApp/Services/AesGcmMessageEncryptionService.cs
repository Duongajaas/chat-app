using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ChatApp.Services;

public class AesGcmMessageEncryptionService : IMessageEncryptionService
{
    private const string VersionPrefix = "v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const string SafeDecryptionFailureMessage = "[Nội dung không thể giải mã]";

    private readonly byte[] _key;
    private readonly ILogger<AesGcmMessageEncryptionService>? _logger;

    public static readonly AesGcmMessageEncryptionService Default = new(ResolveKey(null), null);

    public AesGcmMessageEncryptionService(IConfiguration? configuration = null, ILogger<AesGcmMessageEncryptionService>? logger = null)
        : this(ResolveKey(configuration?["Encryption:MessageKey"]), logger)
    {
    }

    public AesGcmMessageEncryptionService(byte[] key, ILogger<AesGcmMessageEncryptionService>? logger = null)
    {
        if (key.Length != 32)
            throw new ArgumentException("AES-256 key must be exactly 32 bytes (256 bits).", nameof(key));
        _key = [.. key];
        _logger = logger;
    }

    public static byte[] ResolveKey(string? rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return SHA256.HashData(Encoding.UTF8.GetBytes("ChatApp-Default-AES256GCM-MessageEncryptionKey-2026"));
        }

        try
        {
            var b64 = Convert.FromBase64String(rawKey);
            if (b64.Length == 32) return b64;
        }
        catch
        {
            // Not valid Base64, fallback to UTF-8 / SHA256
        }

        var utf8 = Encoding.UTF8.GetBytes(rawKey);
        if (utf8.Length == 32) return utf8;
        return SHA256.HashData(utf8);
    }

    public string Encrypt(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext ?? string.Empty;

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using (var aesGcm = new AesGcm(_key, TagSize))
        {
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        }

        var payload = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, payload, NonceSize + TagSize, ciphertext.Length);

        return VersionPrefix + Convert.ToBase64String(payload);
    }

    public string Decrypt(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return ciphertext ?? string.Empty;

        // Backward compatibility: If it doesn't have the version prefix, it's plaintext
        if (!ciphertext.StartsWith(VersionPrefix, StringComparison.Ordinal))
        {
            return ciphertext;
        }

        try
        {
            var rawBase64 = ciphertext[VersionPrefix.Length..];
            var payload = Convert.FromBase64String(rawBase64);

            if (payload.Length < NonceSize + TagSize)
            {
                _logger?.LogWarning("Ciphertext payload is shorter than nonce + tag size.");
                return SafeDecryptionFailureMessage;
            }

            var nonce = payload.AsSpan(0, NonceSize);
            var tag = payload.AsSpan(NonceSize, TagSize);
            var cipherSpan = payload.AsSpan(NonceSize + TagSize);
            var plaintextBytes = new byte[cipherSpan.Length];

            using (var aesGcm = new AesGcm(_key, TagSize))
            {
                aesGcm.Decrypt(nonce, cipherSpan, tag, plaintextBytes);
            }

            return Encoding.UTF8.GetString(plaintextBytes);
        }
        catch (CryptographicException)
        {
            // Tag mismatch or corrupted data - do not log sensitive data or key
            _logger?.LogWarning("Failed to decrypt message: cryptographic verification failed.");
            return SafeDecryptionFailureMessage;
        }
        catch (Exception)
        {
            _logger?.LogWarning("Failed to decrypt message: invalid format.");
            return SafeDecryptionFailureMessage;
        }
    }
}
