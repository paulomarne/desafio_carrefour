using System;
using System.Threading;
using System.Threading.Tasks;
using FluxoDeCaixa.Core.Interfaces;

namespace FluxoDeCaixa.Application.UseCases.ConsolidadoDiario.Queries
{
    public record ConsolidadoDiarioResponse(
        DateTime Data,
        string Conta,
        decimal TotalDebitos,
        decimal TotalCreditos,
        decimal SaldoLiquido,
        DateTime AtualizadoEm);

    public class ObterConsolidadoDiarioQuery
    {
        private readonly IConsolidadoCache _cache;
        private readonly IConsolidadoRepository _repository;

        public ObterConsolidadoDiarioQuery(IConsolidadoCache cache, IConsolidadoRepository repository)
        {
            _cache = cache;
            _repository = repository;
        }

        public async Task<ConsolidadoDiarioResponse> ExecutarAsync(DateTime data, string conta, CancellationToken cancellationToken = default)
        {
            var consolidado = await _cache.ObterAsync(data, conta);
            if (consolidado is null)
            {
                consolidado = await _repository.ObterPorDataAsync(data, conta);
                if (consolidado is not null)
                {
                    await _cache.SalvarAsync(consolidado);
                }
            }

            if (consolidado is null)
            {
                return new ConsolidadoDiarioResponse(data.Date, conta, 0, 0, 0, DateTime.UtcNow);
            }

            return new ConsolidadoDiarioResponse(
                consolidado.Data,
                consolidado.Conta,
                consolidado.TotalDebitos,
                consolidado.TotalCreditos,
                consolidado.Saldo,
                DateTime.UtcNow);
        }
    }
}
