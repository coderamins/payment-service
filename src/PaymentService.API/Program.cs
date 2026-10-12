using Asp.Versioning;
using Microsoft.EntityFrameworkCore;
using PaymentService.API.Exceptions;
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

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
}).AddMvc();

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("PaymentGateways:Mellat:DisableSslValidationForLocalTestingOnly"))
{
    app.Logger.LogWarning(
        "⚠️ اعتبارسنجی SSL برای درگاه ملت غیرفعال است. این تنظیم فقط برای تست لوکال است و هرگز نباید در Production فعال باشد.");
}


using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    dbContext.Database.Migrate();
}

//if (app.Environment.IsDevelopment())
//{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Payment Service API v1");
    });
//}
app.UseCors("payment-cors-policy");
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
