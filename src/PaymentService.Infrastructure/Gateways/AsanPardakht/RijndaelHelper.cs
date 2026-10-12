namespace PaymentService.Infrastructure.Gateways.AsanPardakht;

using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;

/// <summary>
/// پیاده‌سازی Rijndael با بلاک ۲۵۶ بیتی، طبق مستندات آسان پرداخت.
/// این الگوریتم با AES استاندارد (که همیشه بلاک ۱۲۸ بیتی دارد) فرق دارد،
/// برای همین از BouncyCastle استفاده می‌شود نه System.Security.Cryptography.
///
/// نکته مهم: Mode (CBC) و Padding (PKCS7) اینجا بر اساس رایج‌ترین پیاده‌سازی
/// این الگوریتم در درگاه‌های ایرانی فرض شده‌اند. اگر آسان پرداخت تنظیمات دیگری
/// اعلام کرد (مثلاً ECB)، فقط کافی است RijndaelEngine در سازنده‌ی CbcBlockCipher
/// را با خود RijndaelEngine جایگزین کنید.
/// </summary>
public static class RijndaelHelper
{
    private const int BlockSizeBits = 256;

    public static string Encrypt(string plainText, byte[] key, byte[] iv)
    {
        var cipher = CreateCipher(forEncryption: true, key, iv);
        var input = Encoding.UTF8.GetBytes(plainText);
        var output = ProcessBytes(cipher, input);
        return Convert.ToBase64String(output);
    }

    public static string Decrypt(string base64CipherText, byte[] key, byte[] iv)
    {
        var cipher = CreateCipher(forEncryption: false, key, iv);
        var input = Convert.FromBase64String(base64CipherText);
        var output = ProcessBytes(cipher, input);
        return Encoding.UTF8.GetString(output);
    }

    private static IBufferedCipher CreateCipher(bool forEncryption, byte[] key, byte[] iv)
    {
        var engine = new RijndaelEngine(BlockSizeBits);
        var blockCipher = new CbcBlockCipher(engine);
        var cipher = new PaddedBufferedBlockCipher(blockCipher, new Pkcs7Padding());

        cipher.Init(forEncryption, new ParametersWithIV(new KeyParameter(key), iv));
        return cipher;
    }

    private static byte[] ProcessBytes(IBufferedCipher cipher, byte[] input)
    {
        var output = new byte[cipher.GetOutputSize(input.Length)];
        var length = cipher.ProcessBytes(input, 0, input.Length, output, 0);
        length += cipher.DoFinal(output, length);

        var result = new byte[length];
        Array.Copy(output, result, length);
        return result;
    }
}