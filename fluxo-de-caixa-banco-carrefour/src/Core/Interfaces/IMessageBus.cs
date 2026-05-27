using System;
using System.Threading;
using System.Threading.Tasks;

namespace FluxoDeCaixa.Core.Interfaces
{
    public interface IMessageBus
    {
        Task PublishAsync<TEvent>(TEvent @event, string queueName, CancellationToken cancellationToken = default);
        Task SubscribeAsync<TEvent>(string queueName, Func<TEvent, Task> handler, CancellationToken cancellationToken = default);
    }
}
