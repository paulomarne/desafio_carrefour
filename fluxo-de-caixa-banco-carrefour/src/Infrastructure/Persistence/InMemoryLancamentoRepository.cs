using System.Collections.Generic;
using System.Threading.Tasks;
using FluxoDeCaixa.Core.Entities;
using FluxoDeCaixa.Core.Interfaces;

namespace FluxoDeCaixa.Infrastructure.Persistence
{
    public class InMemoryLancamentoRepository : ILancamentoRepository
    {
        private readonly List<Lancamento> _lancamentos = new();

        public Task AdicionarAsync(Lancamento lancamento)
        {
            _lancamentos.Add(lancamento);
            return Task.CompletedTask;
        }
    }
}
