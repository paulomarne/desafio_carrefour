using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using FluxoDeCaixa.Core.Entities;
using FluxoDeCaixa.Core.Interfaces;

namespace FluxoDeCaixa.Infrastructure.Persistence
{
    public class InMemoryConsolidadoRepository : IConsolidadoRepository
    {
        private readonly ConcurrentDictionary<string, ConsolidadoDiario> _consolidados = new();

        public Task<ConsolidadoDiario?> ObterPorDataAsync(DateTime data, string conta)
        {
            var key = BuildKey(data, conta);
            _consolidados.TryGetValue(key, out var consolidado);
            return Task.FromResult(consolidado);
        }

        public Task SalvarAsync(ConsolidadoDiario consolidado)
        {
            var key = BuildKey(consolidado.Data, consolidado.Conta);
            _consolidados.AddOrUpdate(key, consolidado, (_, _) => consolidado);
            return Task.CompletedTask;
        }

        private static string BuildKey(DateTime data, string conta)
        {
            return $"{data:yyyy-MM-dd}:{conta}";
        }
    }
}
