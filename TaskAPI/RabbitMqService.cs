using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

public interface IRabbitMqService
{
    Task PublishTaskCompletedAsync(TaskCompletedMessage message, CancellationToken ct = default);
    Task CloseAsync();
}

public record TaskCompletedMessage(
    Guid TaskId,
    string Title,
    DateTimeOffset CompletedAt,
    string Priority);

public class RabbitMqService : IRabbitMqService, IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly ILogger<RabbitMqService> _logger;
    private readonly SemaphoreSlim _publishLock = new(1, 1);

    private const string ExchangeName = "task.events";
    private const string RoutingKey = "task.completed";

    public RabbitMqService(IConfiguration config, ILogger<RabbitMqService> logger)
    {
        _logger = logger;

        var factory = new ConnectionFactory
        {
            HostName = config["RabbitMq:Host"] ?? "localhost",
            Port = int.Parse(config["RabbitMq:Port"] ?? "5672"),
            UserName = config["RabbitMq:User"] ?? "guest",
            Password = config["RabbitMq:Password"] ?? "guest"
        };

        _connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
        _channel = _connection.CreateChannelAsync().GetAwaiter().GetResult();

        _channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false).GetAwaiter().GetResult();

        _logger.LogInformation("RabbitMQ connection established, exchange '{Exchange}' declared", ExchangeName);
    }

    public async Task PublishTaskCompletedAsync(TaskCompletedMessage message, CancellationToken ct = default)
    {
        // HACK: не ждём confirms, публикуем "fire and forget", но с логом ошибок.
        // Если RabbitMQ недоступен — логируем и идём дальше (fail silently).
        await _publishLock.WaitAsync(ct);
        try
        {
            var json = JsonSerializer.Serialize(message);
            var body = Encoding.UTF8.GetBytes(json);

            var props = new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent
            };

            await _channel.BasicPublishAsync(
                exchange: ExchangeName,
                routingKey: RoutingKey,
                mandatory: false,
                basicProperties: props,
                body: body,
                cancellationToken: ct);

            _logger.LogInformation("Published task.completed for {TaskId}", message.TaskId);
        }
        catch (Exception ex)
        {
            // fail silently, но с логом
            _logger.LogError(ex, "Failed to publish task.completed for {TaskId}", message.TaskId);
        }
        finally
        {
            _publishLock.Release();
        }
    }

    public async Task CloseAsync()
    {
        try
        {
            if (_channel is not null)
                await _channel.CloseAsync();
            if (_connection is not null)
                await _connection.CloseAsync();
            _logger.LogInformation("RabbitMQ connection closed gracefully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while closing RabbitMQ connection");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        _channel?.Dispose();
        _connection?.Dispose();
        _publishLock.Dispose();
    }
}