using retry_service.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:3003");

builder.Services.AddSingleton<RetryMetricsService>();
builder.Services.AddHostedService<RetryConsumerService>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger(); app.UseSwaggerUI();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "retry-service", port = 3003 }));
app.MapGet("/status", (RetryMetricsService metrics) => Results.Ok(metrics.GetMetrics()));

app.Run();
