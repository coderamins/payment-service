using Microsoft.Extensions.Logging;
using PaymentService.Application.Abstractions;
using RabbitMQ.Client;
using System.Text;

namespace PaymentService.Infrastructure.Messaging;

public class RabbitMqMessagePublisher : IMessagePublisher, IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqMessagePublisher> _logger;
    private readonly Lazy<IConnection> _connection;
    private readonly Lazy<IModel> _channel;

    public RabbitMqMessagePublisher(RabbitMqOptions options, ILogger<RabbitMqMessagePublisher> logger)
    {
        _options = options;
        _logger = logger;

        _connection = new Lazy<IConnection>(CreateConnection);
        _channel = new Lazy<IModel>(CreateChannel);
    }

    public Task PublishAsync(string eventType, string payloadJson, CancellationToken cancellationToken = default)
    {
        var body = Encoding.UTF8.GetBytes(payloadJson);

        var properties = _channel.Value.CreateBasicProperties();
        properties.Persistent = true; // پیام روی دیسک بروکر هم نگه داشته شود تا با ری‌استارت RabbitMQ از بین نرود
        properties.ContentType = "application/json";
        properties.Type = eventType;

        _channel.Value.BasicPublish(
            exchange: _options.ExchangeName,
            routingKey: eventType,
            basicProperties: properties,
            body: body);

        return Task.CompletedTask;
    }

    private IConnection CreateConnection()
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            AutomaticRecoveryEnabled = true // در صورت قطعی موقت شبکه، اتصال خودکار بازیابی شود
        };
        return factory.CreateConnection();
    }

    private IModel CreateChannel()
    {
        var channel = _connection.Value.CreateModel();
        channel.ExchangeDeclare(_options.ExchangeName, ExchangeType.Topic, durable: true);
        return channel;
    }

    public void Dispose()
    {
        if (_channel.IsValueCreated) _channel.Value.Dispose();
        if (_connection.IsValueCreated) _connection.Value.Dispose();
    }
}