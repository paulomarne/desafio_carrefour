using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluxoDeCaixa.Core.Entities;
using FluxoDeCaixa.Core.Interfaces;

namespace FluxoDeCaixa.Infrastructure.Persistence
{
    public class InMemoryLancamentoRepository : ILancamentoRepository
    {
        private readonly ConcurrentDictionary<Guid, Lancamento> _lancamentos = new();

        public Task AdicionarAsync(Lancamento lancamento, CancellationToken cancellationToken = default)
        {
            _lancamentos[lancamento.Id] = lancamento;
            return Task.CompletedTask;
        }

        public Task<Lancamento?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _lancamentos.TryGetValue(id, out var lancamento);
            return Task.FromResult(lancamento);
        }

        public Task<IEnumerable<Lancamento>> ObterPorDataAsync(DateTime data, CancellationToken cancellationToken = default)
        {
            var resultados = _lancamentos.Values
                .Where(x => x.DataLancamento.Date == data.Date)
                .ToList();
            return Task.FromResult<IEnumerable<Lancamento>>(resultados);
        }

        public Task AtualizarAsync(Lancamento lancamento, CancellationToken cancellationToken = default)
        {
            _lancamentos[lancamento.Id] = lancamento;
            return Task.CompletedTask;
        }
    }
}
