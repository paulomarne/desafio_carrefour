using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluxoDeCaixa.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace FluxoDeCaixa.Infrastructure.Messaging
{
    /// <summary>
    /// Implementação in-process do IMessageBus para PoC local (sem RabbitMQ).
    /// Em produção substituir por RabbitMqMessageBus via DI.
    /// </summary>
    public class InMemoryMessageBus : IMessageBus
    {
        private readonly ILogger<InMemoryMessageBus> _logger;
        private readonly ConcurrentDictionary<string, List<Func<string, Task>>> _handlers = new();

        public InMemoryMessageBus(ILogger<InMemoryMessageBus> logger)
        {
            _logger = logger;
        }

        public async Task PublishAsync<TEvent>(TEvent @event, string queueName, CancellationToken cancellationToken = default)
        {
            var payload = JsonSerializer.Serialize(@event);
            _logger.LogInformation("[InMemoryBus] Publicando na fila '{Queue}': {Payload}", queueName, payload);

            if (_handlers.TryGetValue(queueName, out var handlers))
            {
                foreach (var handler in handlers)
                {
                    await handler(payload);
                }
            }
        }

        public Task SubscribeAsync<TEvent>(string queueName, Func<TEvent, Task> handler, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("[InMemoryBus] Inscrito na fila '{Queue}'", queueName);

            var handlers = _handlers.GetOrAdd(queueName, _ => new List<Func<string, Task>>());
            handlers.Add(async payload =>
            {
                var @event = JsonSerializer.Deserialize<TEvent>(payload, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (@event is not null)
                    await handler(@event);
            });

            return Task.CompletedTask;
        }
    }
}
