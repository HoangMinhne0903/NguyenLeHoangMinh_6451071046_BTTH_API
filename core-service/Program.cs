using core_service.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:3002");

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<KafkaProducerService>();
builder.Services.AddSingleton<SpamDetectionService>();
builder.Services.AddHttpClient<AiClassificationService>();
builder.Services.AddHostedService<CoreEventConsumerService>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "core-service", port = 3002 }));

app.Run();
