using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using backend_api.Data;
using shared_models;

namespace backend_api.Services
{
    /// <summary>
    /// Background service that consumes reply_commands and send_retry topics.
    /// Checks idempotency key before calling Facebook API.
    /// Publishes send_failed if Facebook call fails.
    /// </summary>
    public class CommandConsumerService : BackgroundService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<CommandConsumerService> _logger;
        private readonly IServiceProvider _serviceProvider;

        public CommandConsumerService(
            IConfiguration configuration,
            ILogger<CommandConsumerService> logger,
            IServiceProvider serviceProvider)
        {
            _configuration = configuration;
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";

            var config = new ConsumerConfig
            {
                BootstrapServers = bootstrapServers,
                GroupId = "backend-api-consumer",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false
            };

            using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();
            consumer.Subscribe(new[] { "reply_commands", "send_retry" });

            _logger.LogInformation("CommandConsumerService started. Listening to topics 'reply_commands' and 'send_retry'...");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var consumeResult = consumer.Consume(stoppingToken);
                        if (consumeResult?.Message == null) continue;

                        var messageValue = consumeResult.Message.Value;
                        _logger.LogInformation("Consumed from {Topic}: {Message}", consumeResult.Topic, messageValue);

                        var command = JsonSerializer.Deserialize<CommandEvent>(
                            messageValue, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (command != null)
                        {
                            using var scope = _serviceProvider.CreateScope();
                            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                            var facebookApi = scope.ServiceProvider.GetRequiredService<FacebookApiService>();
                            var kafkaProducer = scope.ServiceProvider.GetRequiredService<KafkaProducerService>();

                            await ProcessCommandAsync(command, dbContext, facebookApi, kafkaProducer);
                        }

                        consumer.Commit(consumeResult);
                    }
                    catch (ConsumeException e)
                    {
                        _logger.LogError("Consume error: {Error}", e.Error.Reason);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected error processing command.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("CommandConsumerService is stopping.");
            }
            finally
            {
                consumer.Close();
            }
        }

        private async Task ProcessCommandAsync(
            CommandEvent command,
            AppDbContext dbContext,
            FacebookApiService facebookApi,
            KafkaProducerService kafkaProducer)
        {
            // 1. Skip commands that were already processed.
            var existingKey = await dbContext.IdempotencyKeys
                .FirstOrDefaultAsync(k => k.CommandId == command.CommandId);

            if (existingKey != null)
            {
                _logger.LogInformation("Command {CommandId} already processed (idempotency check). Skipping.", command.CommandId);
                return;
            }

            // 2. Execute Facebook API call
            try
            {
                switch (command.Action)
                {
                    case "reply_message":
                        await facebookApi.SendMessageAsync(command.TargetId, command.Payload);
                        break;

                    case "reply_comment":
                        await facebookApi.ReplyToCommentAsync(command.TargetId, command.Payload);
                        break;

                    case "hide_comment":
                        await facebookApi.HideCommentAsync(command.TargetId);
                        break;

                    case "block_user":
                        _logger.LogWarning("Block user {UserId} — should be done manually by admin on Facebook Page.", command.TargetId);
                        break;

                    default:
                        _logger.LogWarning("Unknown action: {Action}", command.Action);
                        break;
                }

                // 3. Save idempotency key on success
                dbContext.IdempotencyKeys.Add(new IdempotencyKey
                {
                    CommandId = command.CommandId,
                    Status = "success",
                    ProcessedAt = DateTime.UtcNow
                });

                // Update event tracking
                var tracking = await dbContext.EventTrackings.FirstOrDefaultAsync(e => e.EventId == command.EventId);
                if (tracking != null)
                {
                    tracking.State = "replied";
                    tracking.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    dbContext.EventTrackings.Add(new EventTracking
                    {
                        EventId = command.EventId,
                        State = "replied",
                        Metadata = $"action={command.Action}",
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                await dbContext.SaveChangesAsync();
                _logger.LogInformation("Command {CommandId} executed successfully. Idempotency key saved.", command.CommandId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute command {CommandId} (action: {Action}). Publishing to send_failed.",
                    command.CommandId, command.Action);

                // Insert or update the failed command record in SQL Server.
                try
                {
                    var existingFailed = await dbContext.FailedCommands
                        .FirstOrDefaultAsync(fc => fc.CommandId == command.CommandId);

                    if (existingFailed != null)
                    {
                        existingFailed.ErrorMessage = ex.Message + (ex.InnerException != null ? " | " + ex.InnerException.Message : "");
                        existingFailed.FailedAt = DateTime.UtcNow;
                        _logger.LogInformation("✅ Đã cập nhật lệnh lỗi {CommandId} vào bảng FailedCommands.", command.CommandId);
                    }
                    else
                    {
                        var failedCommand = new FailedCommand
                        {
                            CommandId = command.CommandId,
                            EventId = command.EventId,
                            Action = command.Action,
                            TargetId = command.TargetId,
                            Payload = command.Payload,
                            ErrorMessage = ex.Message + (ex.InnerException != null ? " | " + ex.InnerException.Message : ""),
                            FailedAt = DateTime.UtcNow
                        };
                        dbContext.FailedCommands.Add(failedCommand);
                        _logger.LogInformation("✅ Đã ghi nhận lệnh lỗi {CommandId} vào bảng FailedCommands.", command.CommandId);
                    }

                    await dbContext.SaveChangesAsync();
                }
                catch (Exception dbEx)
                {
                    _logger.LogError(dbEx, "Lỗi khi ghi nhận/cập nhật FailedCommand {CommandId} vào database.", command.CommandId);
                }

                // 4. Publish to send_failed for Retry Service
                await kafkaProducer.PublishAsync("send_failed", command, command.CommandId);
            }
        }
    }
}
