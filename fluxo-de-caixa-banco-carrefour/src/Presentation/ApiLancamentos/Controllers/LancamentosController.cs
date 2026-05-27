using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluxoDeCaixa.Application.UseCases.Lancamentos.Commands;
using FluxoDeCaixa.Core.Entities;
using FluxoDeCaixa.Core.Interfaces;

namespace FluxoDeCaixa.ApiLancamentos.Controllers
{
    public record LancamentoResponse(
        Guid Id,
        string Tipo,
        string Conta,
        decimal Valor,
        string Descricao,
        DateTime DataLancamento,
        DateTime DataCriacao,
        string Status);

    [ApiController]
    [Route("api/[controller]")]
    public class LancamentosController : ControllerBase
    {
        private readonly CriarLancamentoCommand _criarCommand;
        private readonly CancelarLancamentoCommand _cancelarCommand;
        private readonly ILancamentoRepository _repository;

        public LancamentosController(
            CriarLancamentoCommand criarCommand,
            CancelarLancamentoCommand cancelarCommand,
            ILancamentoRepository repository)
        {
            _criarCommand = criarCommand;
            _cancelarCommand = cancelarCommand;
            _repository = repository;
        }

        /// <summary>Registra um novo lançamento de débito ou crédito.</summary>
        [HttpPost]
        [ProducesResponseType(typeof(CriarLancamentoResult), 201)]
        [ProducesResponseType(typeof(ProblemDetails), 400)]
        public async Task<IActionResult> Post([FromBody] CriarLancamentoRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var resultado = await _criarCommand.ExecutarAsync(request, cancellationToken);
                return CreatedAtAction(nameof(GetPorData),
                    new { data = resultado.DataLancamento.ToString("yyyy-MM-dd") },
                    resultado);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Dados inválidos",
                    Detail = ex.Message,
                    Status = 400
                });
            }
        }

        /// <summary>Lista lançamentos de uma data específica.</summary>
        [HttpGet("{data:datetime}")]
        [ProducesResponseType(typeof(LancamentoResponse[]), 200)]
        public async Task<IActionResult> GetPorData(DateTime data, CancellationToken cancellationToken)
        {
            var lancamentos = await _repository.ObterPorDataAsync(data, cancellationToken);
            var response = lancamentos.Select(l => new LancamentoResponse(
                l.Id, l.Tipo.ToString(), l.Conta, l.Valor,
                l.Descricao, l.DataLancamento, l.DataCriacao, l.Status.ToString()));
            return Ok(response);
        }

        /// <summary>Cancela um lançamento existente (soft-delete).</summary>
        [HttpDelete("{id:guid}")]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ProblemDetails), 400)]
        [ProducesResponseType(typeof(ProblemDetails), 404)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            try
            {
                await _cancelarCommand.ExecutarAsync(id, cancellationToken);
                return NoContent();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("não encontrado"))
            {
                return NotFound(new ProblemDetails
                {
                    Title = "Lançamento não encontrado",
                    Detail = ex.Message,
                    Status = 404
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Operação inválida",
                    Detail = ex.Message,
                    Status = 400
                });
            }
        }
    }
}
