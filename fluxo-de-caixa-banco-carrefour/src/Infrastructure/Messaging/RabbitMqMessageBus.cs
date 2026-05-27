using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluxoDeCaixa.Core.Interfaces;
using FluxoDeCaixa.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FluxoDeCaixa.Infrastructure.Messaging
{
    /// <summary>
    /// Implementação do IMessageBus usando RabbitMQ (API v7 async).
    /// Para uso em produção/Docker. Em PoC local usar InMemoryMessageBus.
    /// </summary>
    public class RabbitMqMessageBus : IMessageBus, IAsyncDisposable
    {
        private readonly ILogger<RabbitMqMessageBus> _logger;
        private IConnection? _connection;
        private bool _initialized;

        public RabbitMqMessageBus(ILogger<RabbitMqMessageBus> logger)
        {
            _logger = logger;
        }

        private async Task EnsureConnectedAsync()
        {
            if (_initialized) return;

            var hostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "rabbitmq";
            var userName = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "guest";
            var password = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "guest";
            var port = int.TryParse(Environment.GetEnvironmentVariable("RABBITMQ_PORT"), out var p) ? p : 5672;

            var factory = new ConnectionFactory
            {
                HostName = hostName,
                UserName = userName,
                Password = password,
                Port = port
            };

            _connection = await factory.CreateConnectionAsync();
            _initialized = true;
            _logger.LogInformation("[RabbitMQ] Conectado em {Host}:{Port}", hostName, port);
        }

        public async Task PublishAsync<TEvent>(TEvent @event, string queueName, CancellationToken cancellationToken = default)
        {
            await RetryPolicy.ExecuteAsync(async () =>
            {
                await EnsureConnectedAsync();

                await using var channel = await _connection!.CreateChannelAsync(cancellationToken: cancellationToken);

                await channel.QueueDeclareAsync(
                    queue: queueName, durable: true, exclusive: false,
                    autoDelete: false, arguments: null,
                    cancellationToken: cancellationToken);

                var body = Serialize(@event);
                var props = new BasicProperties { Persistent = true };

                await channel.BasicPublishAsync(
                    exchange: string.Empty,
                    routingKey: queueName,
                    mandatory: false,
                    basicProperties: props,
                    body: body,
                    cancellationToken: cancellationToken);
            });
        }

        public async Task SubscribeAsync<TEvent>(string queueName, Func<TEvent, Task> handler, CancellationToken cancellationToken = default)
        {
            await EnsureConnectedAsync();

            var channel = await _connection!.CreateChannelAsync(cancellationToken: cancellationToken);

            await channel.QueueDeclareAsync(
                queue: queueName, durable: true, exclusive: false,
                autoDelete: false, arguments: null,
                cancellationToken: cancellationToken);

            await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: cancellationToken);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, eventArgs) =>
            {
                try
                {
                    var message = Deserialize<TEvent>(eventArgs.Body.ToArray());
                    await handler(message);
                    await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[RabbitMQ] Erro ao processar mensagem na fila {Queue}", queueName);
                    await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: false);
                }
            };

            await channel.BasicConsumeAsync(queue: queueName, autoAck: false, consumer: consumer, cancellationToken: cancellationToken);
        }

        private static byte[] Serialize<TEvent>(TEvent @event) =>
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(@event, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

        private static TEvent Deserialize<TEvent>(byte[] body) =>
            JsonSerializer.Deserialize<TEvent>(Encoding.UTF8.GetString(body), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })!;

        public async ValueTask DisposeAsync()
        {
            if (_connection is not null)
            {
                await _connection.CloseAsync();
                await _connection.DisposeAsync();
            }
        }
    }
}
