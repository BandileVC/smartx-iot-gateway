using System.Security.Cryptography;

namespace SmartX.Shared.Services;

/// <summary>
/// Encrypts/decrypts attached device files (config files, deployment photos, logs)
/// at rest using AES-256. A random IV is generated per file and prepended to the
/// ciphertext so each encrypted file is self-contained and independently decryptable.
/// The key is derived once per API process from configuration (see Program.cs) —
/// in production this would come from a secrets manager / key vault rather than
/// appsettings.
/// </summary>
public class FileEncryptionService
{
    private readonly byte[] _key;

    public FileEncryptionService(string base64Key)
    {
        _key = Convert.FromBase64String(base64Key);
    }

    public static string GenerateBase64Key()
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.GenerateKey();
        return Convert.ToBase64String(aes.Key);
    }

    public async Task EncryptToFileAsync(Stream input, string destinationPath)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        await using var output = File.Create(destinationPath);
        // Store the IV as a clear-text prefix (standard practice - IV need not be secret).
        await output.WriteAsync(aes.IV);

        await using var cryptoStream = new CryptoStream(output, aes.CreateEncryptor(), CryptoStreamMode.Write);
        await input.CopyToAsync(cryptoStream);
    }

    public async Task<byte[]> DecryptFromFileAsync(string sourcePath)
    {
        await using var input = File.OpenRead(sourcePath);
        var iv = new byte[16];
        var read = await input.ReadAsync(iv.AsMemory(0, 16));
        if (read != 16) throw new InvalidDataException("Encrypted file is missing its IV header.");

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = iv;

        await using var cryptoStream = new CryptoStream(input, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var ms = new MemoryStream();
        await cryptoStream.CopyToAsync(ms);
        return ms.ToArray();
    }
}
