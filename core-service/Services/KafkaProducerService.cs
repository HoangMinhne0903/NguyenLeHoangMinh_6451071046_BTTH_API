using Confluent.Kafka;
using System.Text.Json;

namespace core_service.Services;

public sealed class KafkaProducerService : IDisposable
{
    private readonly IProducer<string, string> _producer;

    public KafkaProducerService(IConfiguration configuration)
    {
        var bootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        var config = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public Task PublishAsync<T>(string topic, T payload, string? key = null)
    {
        var message = new Message<string, string>
        {
            Key = key ?? Guid.NewGuid().ToString(),
            Value = JsonSerializer.Serialize(payload)
        };

        return _producer.ProduceAsync(topic, message);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
