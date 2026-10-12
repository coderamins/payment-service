using System.Security.Cryptography;
using System.Text;

namespace PaymentService.Infrastructure.Gateways.Helpers;

public static class Aes256CbcHelper
{
    public static string Encrypt(string nationalId,string randomSalt, string encryptionKeyBase64, string encryptionIvBase64)
    {
        string plainText = $"0|{nationalId}|{randomSalt}|";

        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);

        byte[] key = Convert.FromBase64String(encryptionKeyBase64);
        byte[] iv = Convert.FromBase64String(encryptionIvBase64);

        if (key.Length != 16)
            throw new ArgumentException("Encryption Key باید 16 بایت باشد.");

        if (iv.Length != 16)
            throw new ArgumentException("Encryption IV باید 16 بایت باشد.");

        using var aes = Aes.Create();

        aes.KeySize = 128;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        aes.Key = key;
        aes.IV = iv;

        using var encryptor = aes.CreateEncryptor();

        byte[] encryptedBytes = encryptor.TransformFinalBlock(
            plainBytes,
            0,
            plainBytes.Length);

        return Convert.ToBase64String(encryptedBytes);
    }
}