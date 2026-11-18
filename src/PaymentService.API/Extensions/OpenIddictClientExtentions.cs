using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

namespace PaymentService.API.Extensions;

public static class OpenIddictClientExtentions
{
    public static void AddOpenIddictClientConfig(this IServiceCollection services, IConfiguration configuration)
    {
        var publicKeyBytes = File.ReadAllBytes(ResolvePublicKeyPath());
        var rsa = RSA.Create();
        rsa.ImportRSAPublicKey(publicKeyBytes, out _);
        var validationKey = new RsaSecurityKey(rsa);

        var validIssuers = configuration.GetSection("OpenIdditOptions").Get<OpenIdditOptions>();

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme; 
        })
        .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = validIssuers?.ValidIssuers,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = validationKey
            };

            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    Console.WriteLine($"❌ Authentication failed: {context.Exception.Message}");
                    return Task.CompletedTask;
                },
                OnChallenge = context =>
                {
                    Console.WriteLine($"⚠️ Challenge triggered: {context.Error}, {context.ErrorDescription}");
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    Console.WriteLine("✅ Token validated successfully.");
                    return Task.CompletedTask;
                }
            };
        });

        services.AddAuthorization();

    }

    private static string ResolvePublicKeyPath()
    {
        const string relativePath = "Keys/public_key.der";

        var fromBaseDirectory = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (File.Exists(fromBaseDirectory))
            return fromBaseDirectory;

        if (File.Exists(relativePath))
            return relativePath;

        throw new FileNotFoundException(
            "JWT public key not found. Expected 'Keys/public_key.der' in the application base directory.",
            fromBaseDirectory);
    }
}


public class OpenIdditOptions
{
    public List<string> ValidIssuers { get; set; } = [];
    public string MainIssuer { get; set; } = string.Empty;
}