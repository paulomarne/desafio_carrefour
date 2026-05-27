using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxoDeCaixa.Core.Entities;

namespace FluxoDeCaixa.Core.Interfaces
{
    public interface ILancamentoRepository
    {
        Task AdicionarAsync(Lancamento lancamento, CancellationToken cancellationToken = default);
        Task<Lancamento?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<IEnumerable<Lancamento>> ObterPorDataAsync(DateTime data, CancellationToken cancellationToken = default);
        Task AtualizarAsync(Lancamento lancamento, CancellationToken cancellationToken = default);
    }
}
