using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FluxoDeCaixa.Application.UseCases.ConsolidadoDiario.Queries
{
    public record ConsolidadoDiarioResponse(DateTime Data, string Conta, decimal Debito, decimal Credito, decimal Total);

    public class ObterConsolidadoDiarioQuery
    {
        // Aqui injetaríamos um repositório de leitura (Read Model)
        // private readonly IConsolidadoRepository _repository;

        public ObterConsolidadoDiarioQuery()
        {
        }

        public async Task<IEnumerable<ConsolidadoDiarioResponse>> ExecutarAsync()
        {
            // Simulação de retorno para fins de demonstração
            return await Task.FromResult(new List<ConsolidadoDiarioResponse>
            {
                new ConsolidadoDiarioResponse(DateTime.UtcNow.Date, "1", 1000, 2500, 1500)
            });
        }
    }
}
