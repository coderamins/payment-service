using System.Globalization;
using System.Security.Cryptography;

namespace PaymentService.Infrastructure.Gateways.IranKish;

internal static class IranKishHelpers
{
    internal record EnvelopeResult(string Data, string Iv);

    internal static EnvelopeResult CreateAuthenticationEnvelope(string terminalId, string passPhrase, decimal amount, string publicKey)
    {
        var hex = terminalId + passPhrase + amount.ToString("D12", CultureInfo.InvariantCulture) + "00";
        if (hex.Length % 2 != 0) hex = "0" + hex;
        var plain = Convert.FromHexString(hex);
        using var aes = Aes.Create();
        aes.KeySize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateKey();
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        var cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(cipher);
        var envelope = new byte[48];
        Buffer.BlockCopy(aes.Key, 0, envelope, 0, 16);
        Buffer.BlockCopy(hash, 0, envelope, 16, 32);
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
        return new EnvelopeResult(Convert.ToHexString(rsa.Encrypt(envelope, RSAEncryptionPadding.Pkcs1)), Convert.ToHexString(aes.IV));
    }
}
