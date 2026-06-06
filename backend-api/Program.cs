using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using backend_api.Data;
using backend_api.Services;
using Polly;
using Polly.Extensions.Http;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:3000");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<KafkaProducerService>();

builder.Services.AddHttpClient<FacebookApiService>()
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30),
            onBreak: (r, d) => Console.WriteLine($"[CB] OPEN - FB API paused {d.TotalSeconds}s"),
            onReset: () => Console.WriteLine("[CB] CLOSED - FB API resumed"),
            onHalfOpen: () => Console.WriteLine("[CB] HALF-OPEN - Testing FB API...")));

builder.Services.AddHostedService<CommandConsumerService>();

var jwtKey = builder.Configuration["Jwt:Key"] ?? "SuperSecretKeyForWebhookService2026!@#$%";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "webhook-system",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "webhook-dashboard",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // Auto-create FailedCommands table if not exists
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF OBJECT_ID('FailedCommands', 'U') IS NULL
            BEGIN
                CREATE TABLE FailedCommands (
                    CommandId NVARCHAR(450) PRIMARY KEY,
                    EventId NVARCHAR(MAX) NOT NULL,
                    Action NVARCHAR(MAX) NOT NULL,
                    TargetId NVARCHAR(MAX) NOT NULL,
                    Payload NVARCHAR(MAX) NOT NULL,
                    ErrorMessage NVARCHAR(MAX) NOT NULL,
                    FailedAt DATETIME2 NOT NULL
                );
            END");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DB Setup] Warning: Could not run FailedCommands auto-create script: {ex.Message}");
    }
}

app.UseSwagger(); app.UseSwaggerUI();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "backend-api", port = 3000 }));

app.Run();
