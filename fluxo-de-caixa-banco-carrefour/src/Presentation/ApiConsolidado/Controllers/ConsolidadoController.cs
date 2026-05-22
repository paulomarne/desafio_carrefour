using Microsoft.AspNetCore.Mvc;
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

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var resultado = await _query.ExecutarAsync();
            return Ok(resultado);
        }
    }
}
