using System.Security.Cryptography;
using System.Text;

namespace PaymentService.Infrastructure.Gateways.Melli;

public static class MelliSigner
{
    public static string Sign(string data,string base64Key)
    {
        using var tdes = TripleDES.Create();
        tdes.Key=Convert.FromBase64String(base64Key);
        tdes.Mode=CipherMode.ECB;
        tdes.Padding=PaddingMode.PKCS7;

        using var encryptor = tdes.CreateEncryptor();
        var bytes = Encoding.UTF8.GetBytes(data);

        return Convert.ToBase64String(encryptor
            .TransformFinalBlock(bytes,0,bytes.Length));
    }
}
