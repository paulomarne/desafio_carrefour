using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using FluxoDeCaixa.Application.UseCases.Lancamentos.Commands;

namespace FluxoDeCaixa.ApiLancamentos.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LancamentosController : ControllerBase
    {
        private readonly CriarLancamentoCommand _command;

        public LancamentosController(CriarLancamentoCommand command)
        {
            _command = command;
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] CriarLancamentoRequest request)
        {
            await _command.ExecutarAsync(request);
            return Ok(new { message = "Lançamento realizado com sucesso." });
        }
    }
}
