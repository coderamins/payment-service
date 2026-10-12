using PaymentService.Infrastructure.Gateways.Helpers;
using System.Text;

namespace paymentService.UnitTests;

public class AdditionalDataTest
{

    [Fact]
    public void Encrypt_MatchesBankProvidedTestVector()
    {
        var keyBase64 = "M3M2djl5JEImRSlIQE1jUQ==";
        var ivBase64 = "WHAyczV2OHkvQj9FKEcrSw==";

        const int personalityTypeIndividualIranian = 0;

        // رشته‌ی گسترش تصادفی: عددی با ۱ تا ۷ رقم، طبق N(1,7)
        var randomSalt = "12345678";// Random.Shared.Next(10_000_000, 100_000_000);

        var plainText = $"{personalityTypeIndividualIranian}|2181681661|{randomSalt}";

        var key = Convert.FromBase64String(keyBase64);
        var iv = Convert.FromBase64String(ivBase64);

        var encryptedNationalId = Aes256CbcHelper.Encrypt("2181681661", randomSalt.ToString(),
            encryptionKeyBase64: keyBase64,
            encryptionIvBase64: ivBase64);

        

        // Arrange
        plainText = "0|2181681661|12345678|";


        byte[] keyBytes = Convert.FromBase64String(keyBase64);
        byte[] ivBytes = Convert.FromBase64String(ivBase64);
        string n = Random.Shared.Next(1, 10_000_000).ToString();
        randomSalt = "12345678";// Random.Shared.Next(1, 10_000_000);
        // Act
        var result = Aes256CbcHelper.Encrypt(
            "2181681661",
            randomSalt.ToString(),
            keyBase64,
            ivBase64);

        // Assert
        Assert.Equal(
            "8qij95IVToNK7lbBv+PzAPdjJfobsv8P5ZWfb+Ozsto=",
            result);
    }
}
