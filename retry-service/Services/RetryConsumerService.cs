using Confluent.Kafka;
using System.Text.Json;
using shared_models;

namespace retry_service.Services
{
    /// <summary>
    /// Consumes send_failed topic. Applies exponential backoff.
    /// If retry_count < MaxRetries → publish to send_retry.
    /// If retry_count >= MaxRetries → publish to dead_letter.
    /// </summary>
    public class RetryConsumerService : BackgroundService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<RetryConsumerService> _logger;
        private readonly RetryMetricsService _metricsService;
        private const int MaxRetries = 5;

        public RetryConsumerService(IConfiguration configuration, ILogger<RetryConsumerService> logger, RetryMetricsService metricsService)
        {
            _configuration = configuration;
            _logger = logger;
            _metricsService = metricsService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";

            var consumerConfig = new ConsumerConfig
            {
                BootstrapServers = bootstrapServers,
                GroupId = "retry-service-consumer",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false
            };

            var producerConfig = new ProducerConfig
            {
                BootstrapServers = bootstrapServers,
                Acks = Acks.All,
                EnableIdempotence = true
            };

            using var consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();
            using var producer = new ProducerBuilder<string, string>(producerConfig).Build();

            consumer.Subscribe("send_failed");
            _logger.LogInformation("RetryConsumerService started. Listening to 'send_failed'. Max retries: {Max}", MaxRetries);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var consumeResult = consumer.Consume(stoppingToken);
                        if (consumeResult?.Message == null) continue;

                        var command = JsonSerializer.Deserialize<CommandEvent>(
                            consumeResult.Message.Value,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (command == null) { consumer.Commit(consumeResult); continue; }

                        command.RetryCount++;
                        _logger.LogInformation("Processing failed command {CommandId}, retry #{Count}/{Max}",
                            command.CommandId, command.RetryCount, MaxRetries);

                        if (command.RetryCount >= MaxRetries)
                        {
                            // Dead Letter Queue
                            _logger.LogError("Command {CommandId} exceeded max retries ({Max}). Moving to dead_letter.",
                                command.CommandId, MaxRetries);

                            var dlqMessage = JsonSerializer.Serialize(command);
                            await producer.ProduceAsync("dead_letter", new Message<string, string>
                            {
                                Key = command.CommandId,
                                Value = dlqMessage
                            });

                            _metricsService.IncrementDlq(command.CommandId);
                            _logger.LogError("☠️ DEAD LETTER: Command {CommandId} moved to dead_letter topic.", command.CommandId);
                        }
                        else
                        {
                            // Exponential backoff: delay = 1s * 2^(retryCount-1)
                            var delayMs = (int)(1000 * Math.Pow(2, command.RetryCount - 1));
                            _logger.LogInformation("Waiting {Delay}ms before retry (exponential backoff)...", delayMs);
                            await Task.Delay(delayMs, stoppingToken);

                            // Publish to send_retry for Backend API
                            var retryMessage = JsonSerializer.Serialize(command);
                            await producer.ProduceAsync("send_retry", new Message<string, string>
                            {
                                Key = command.CommandId,
                                Value = retryMessage
                            });

                            _metricsService.IncrementRetries(command.CommandId);
                            _logger.LogInformation("Republished command {CommandId} to 'send_retry' (attempt {Count})",
                                command.CommandId, command.RetryCount);
                        }

                        consumer.Commit(consumeResult);
                    }
                    catch (ConsumeException e)
                    {
                        _logger.LogError("Consume error: {Error}", e.Error.Reason);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing retry message.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("RetryConsumerService stopping.");
            }
            finally
            {
                consumer.Close();
            }
        }
    }
}
