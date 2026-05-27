using System;
using System.Threading.Tasks;
using FluxoDeCaixa.Core.Entities;

namespace FluxoDeCaixa.Core.Interfaces
{
    public interface IConsolidadoCache
    {
        Task<ConsolidadoDiario?> ObterAsync(DateTime data, string conta);
        Task SalvarAsync(ConsolidadoDiario consolidado);
        Task InvalidateAsync(DateTime data, string conta);
    }
}
