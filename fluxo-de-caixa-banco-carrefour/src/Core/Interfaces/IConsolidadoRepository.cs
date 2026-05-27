using System;
using System.Threading.Tasks;
using FluxoDeCaixa.Core.Entities;

namespace FluxoDeCaixa.Core.Interfaces
{
    public interface IConsolidadoRepository
    {
        Task<ConsolidadoDiario?> ObterPorDataAsync(DateTime data, string conta);
        Task SalvarAsync(ConsolidadoDiario consolidado);
    }
}
