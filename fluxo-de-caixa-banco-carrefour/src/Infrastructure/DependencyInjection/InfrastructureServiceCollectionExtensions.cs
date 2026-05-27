using System;
using FluxoDeCaixa.Core.Interfaces;
using FluxoDeCaixa.Infrastructure.Cache;
using FluxoDeCaixa.Infrastructure.Messaging;
using FluxoDeCaixa.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FluxoDeCaixa.Infrastructure.DependencyInjection
{
    public static class InfrastructureServiceCollectionExtensions
    {
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
        {
            services.AddMemoryCache();

            // Seleciona automaticamente o Message Bus:
            // - RABBITMQ_HOST definido → usa RabbitMQ (Docker/produção)
            // - Sem RABBITMQ_HOST      → usa InMemory (PoC local, dotnet run)
            var rabbitHost = Environment.GetEnvironmentVariable("RABBITMQ_HOST");
            if (!string.IsNullOrWhiteSpace(rabbitHost))
                services.AddSingleton<IMessageBus, RabbitMqMessageBus>();
            else
                services.AddSingleton<IMessageBus, InMemoryMessageBus>();

            services.AddSingleton<IConsolidadoCache, InMemoryConsolidadoCache>();
            services.AddSingleton<IConsolidadoRepository, InMemoryConsolidadoRepository>();
            services.AddSingleton<ILancamentoRepository, InMemoryLancamentoRepository>();

            return services;
        }
    }
}
