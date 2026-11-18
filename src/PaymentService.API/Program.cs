using Asp.Versioning;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PaymentService.API.Exceptions;
using PaymentService.API.Extensions;
using PaymentService.API.Filters;
using PaymentService.Infrastructure.DependencyInjection;
using PaymentService.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Payment Service API",
        Version = "v1"
    });
});
builder.Services.AddPaymentInfrastructure(builder.Configuration);
builder.Services.AddCors(options =>
{
    options.AddPolicy("payment-cors-policy", policy =>
    {
        policy.AllowAnyOrigin();
        policy.AllowAnyMethod();
        policy.AllowAnyHeader();
    });
});

builder.Services.AddScoped<IdempotencyKeyFilter>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddRateLimitConfigs();
builder.Services.AddOpenIddictClientConfig(builder.Configuration);

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
}).AddMvc();

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
    o.KnownIPNetworks.Add(new System.Net.IPNetwork(System.Net.IPAddress.Parse("172.18.0.0"), 16)); 
});

builder.Services.AddProblemDetails();

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
    .AddDbContextCheck<PaymentDbContext>("sqlserver", tags: new[] { "ready" })
    .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: new[] { "ready" });

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("PaymentGateways:Mellat:DisableSslValidationForLocalTestingOnly"))
{
    app.Logger.LogWarning(
        "⚠️ اعتبارسنجی SSL برای درگاه ملت غیرفعال است. این تنظیم فقط برای تست لوکال است و هرگز نباید در Production فعال باشد.");
}


//using (var scope = app.Services.CreateScope())
//{
//    var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
//    dbContext.Database.Migrate();
//}

//if (app.Environment.IsDevelopment())
//{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Payment Service API v1");
    });
//}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseCors("payment-cors-policy");
app.UseHttpsRedirection();

app.UseForwardedHeaders();   
app.UseRouting();            
app.UseAuthentication();
app.UseRateLimiter();       
app.UseAuthorization();
app.MapControllers();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("live")
}).AllowAnonymous().DisableRateLimiting();

app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready")
}).AllowAnonymous().DisableRateLimiting();


app.Run();
