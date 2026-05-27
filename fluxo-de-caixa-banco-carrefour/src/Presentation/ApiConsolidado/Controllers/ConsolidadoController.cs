using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Threading.Tasks;
using FluxoDeCaixa.Application.UseCases.ConsolidadoDiario.Queries;

namespace FluxoDeCaixa.ApiConsolidado.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ConsolidadoController : ControllerBase
    {
        private readonly ObterConsolidadoDiarioQuery _query;

        public ConsolidadoController(ObterConsolidadoDiarioQuery query)
        {
            _query = query;
        }

        /// <summary>Retorna o consolidado diário de uma data específica.</summary>
        [HttpGet("{data:datetime}")]
        [ProducesResponseType(typeof(ConsolidadoDiarioResponse), 200)]
        public async Task<IActionResult> GetPorData(DateTime data, [FromQuery] string conta = "default", CancellationToken cancellationToken = default)
        {
            var resultado = await _query.ExecutarAsync(data.Date, conta, cancellationToken);
            return Ok(resultado);
        }

        /// <summary>Retorna o consolidado do dia atual.</summary>
        [HttpGet("hoje")]
        [ProducesResponseType(typeof(ConsolidadoDiarioResponse), 200)]
        public async Task<IActionResult> GetHoje([FromQuery] string conta = "default", CancellationToken cancellationToken = default)
        {
            var resultado = await _query.ExecutarAsync(DateTime.UtcNow.Date, conta, cancellationToken);
            return Ok(resultado);
        }
    }
}
