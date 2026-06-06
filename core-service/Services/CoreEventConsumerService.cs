using Confluent.Kafka;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using shared_models;

namespace core_service.Services
{
    /// <summary>
    /// Background service that consumes raw_events, runs spam/toxic detection,
    /// fallback or AI classification, then publishes commands to reply_commands.
    /// </summary>
    public class CoreEventConsumerService : BackgroundService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<CoreEventConsumerService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IMemoryCache _dedupCache;

        public CoreEventConsumerService(
            IConfiguration configuration,
            ILogger<CoreEventConsumerService> logger,
            IServiceProvider serviceProvider,
            IMemoryCache dedupCache)
        {
            _configuration = configuration;
            _logger = logger;
            _serviceProvider = serviceProvider;
            _dedupCache = dedupCache;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();

            var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
            var groupId = _configuration["Kafka:GroupId"] ?? "core-event-consumer";

            var config = new ConsumerConfig
            {
                BootstrapServers = bootstrapServers,
                GroupId = groupId,
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false
            };

            using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();
            consumer.Subscribe("raw_events");

            _logger.LogInformation("CoreEventConsumerService started. Listening to topic 'raw_events'...");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var consumeResult = consumer.Consume(stoppingToken);
                        if (consumeResult?.Message == null) continue;

                        var messageValue = consumeResult.Message.Value;
                        _logger.LogInformation("Consumed message from raw_events: {Message}", messageValue);

                        var normalizedEvent = JsonSerializer.Deserialize<NormalizedEvent>(
                            messageValue,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (normalizedEvent != null)
                        {
                            var dedupKey = $"processed_{normalizedEvent.EventId}";
                            if (_dedupCache.TryGetValue(dedupKey, out _))
                            {
                                _logger.LogInformation("Duplicate event {EventId} detected, skipping.", normalizedEvent.EventId);
                                consumer.Commit(consumeResult);
                                continue;
                            }

                            using var scope = _serviceProvider.CreateScope();
                            var spamService = scope.ServiceProvider.GetRequiredService<SpamDetectionService>();
                            var aiService = scope.ServiceProvider.GetRequiredService<AiClassificationService>();
                            var kafkaProducer = scope.ServiceProvider.GetRequiredService<KafkaProducerService>();

                            await ProcessEventAsync(normalizedEvent, spamService, aiService, kafkaProducer);

                            _dedupCache.Set(dedupKey, true, TimeSpan.FromHours(24));
                        }

                        consumer.Commit(consumeResult);
                    }
                    catch (ConsumeException e)
                    {
                        _logger.LogError("Consume error: {Error}", e.Error.Reason);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected error processing message.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("CoreEventConsumerService is stopping.");
            }
            finally
            {
                consumer.Close();
            }
        }

        private async Task ProcessEventAsync(
            NormalizedEvent evt,
            SpamDetectionService spam,
            AiClassificationService ai,
            KafkaProducerService kafkaProducer)
        {
            _logger.LogInformation("Processing event {EventId} (type: {EventType})", evt.EventId, evt.EventType);

            if (evt.EventType != "message" && evt.EventType != "comment")
            {
                _logger.LogInformation("Event type '{EventType}' does not require AI processing, skipping.", evt.EventType);
                return;
            }

            if (evt.SenderId == evt.PageId)
            {
                _logger.LogInformation("Skipping self-authored event {EventId} from Page {PageId}.", evt.EventId, evt.PageId);
                return;
            }

            if (spam.IsRateLimited(evt.SenderId))
            {
                _logger.LogWarning(
                    "Event {EventId} from {SenderId} exceeded rate limits and is routed to pending review.",
                    evt.EventId,
                    evt.SenderId);
                return;
            }

            if (spam.IsBlacklisted(evt.SenderId))
            {
                _logger.LogWarning("User {SenderId} is in internal blacklist. Skipping auto-reply.", evt.SenderId);
                return;
            }

            var isSpam = spam.IsSpam(evt.Content);
            var isDuplicate = spam.IsDuplicateContent(evt.SenderId, evt.Content);
            var isSevereToxic = spam.IsSevereToxic(evt.Content);

            if (isSpam || isDuplicate || isSevereToxic)
            {
                var classification = isSevereToxic ? "toxic" : "spam";
                var strikeTriggeredBlacklist = spam.RecordSpamStrikeAndCheckBlacklist(evt.SenderId);

                if (evt.EventType == "comment")
                {
                    var hideCmd = new CommandEvent
                    {
                        EventId = evt.EventId,
                        PageId = evt.PageId,
                        SenderId = evt.SenderId,
                        Action = "hide_comment",
                        TargetId = evt.EventId,
                        Intent = classification,
                        Sentiment = "negative"
                    };

                    await kafkaProducer.PublishAsync("reply_commands", hideCmd, evt.EventId);
                    _logger.LogInformation(
                        "Published hide_comment for {EventId} because comment was classified as {Classification}.",
                        evt.EventId,
                        classification);
                }

                if (strikeTriggeredBlacklist)
                {
                    _logger.LogWarning(
                        "User {SenderId} entered the internal blacklist after repeated spam strikes. Admin review is recommended.",
                        evt.SenderId);
                }

                return;
            }

            try
            {
                var analysis = await ai.AnalyzeContentAsync(evt.Content);
                _logger.LogInformation(
                    "AI analysis for {EventId}: Intent={Intent}, Sentiment={Sentiment}, Confidence={Confidence}, NeedsHumanReview={NeedsHumanReview}",
                    evt.EventId,
                    analysis.Intent,
                    analysis.Sentiment,
                    analysis.Confidence,
                    analysis.NeedsHumanReview);

                if (analysis.NeedsHumanReview)
                {
                    _logger.LogInformation("Event {EventId} needs human review, skipping auto-reply.", evt.EventId);
                    return;
                }

                if (string.IsNullOrWhiteSpace(analysis.ReplyMessage))
                {
                    _logger.LogInformation("Event {EventId} produced no automatic reply.", evt.EventId);
                    return;
                }

                var action = evt.EventType == "message" ? "reply_message" : "reply_comment";
                var targetId = evt.EventType == "message" ? evt.SenderId : evt.EventId;

                var replyCmd = new CommandEvent
                {
                    EventId = evt.EventId,
                    PageId = evt.PageId,
                    SenderId = evt.SenderId,
                    Action = action,
                    TargetId = targetId,
                    Payload = analysis.ReplyMessage,
                    Intent = analysis.Intent,
                    Sentiment = analysis.Sentiment,
                    Confidence = analysis.Confidence,
                    NeedsHumanReview = analysis.NeedsHumanReview
                };

                await kafkaProducer.PublishAsync("reply_commands", replyCmd, replyCmd.CommandId);
                _logger.LogInformation("Published reply command {CommandId} to 'reply_commands'.", replyCmd.CommandId);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "AI API call failed for event {EventId}.", evt.EventId);
            }
        }
    }
}
