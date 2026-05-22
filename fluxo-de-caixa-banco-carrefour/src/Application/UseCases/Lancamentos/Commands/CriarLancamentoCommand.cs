using System.Threading.Tasks;
using FluxoDeCaixa.Core.Entities;
using FluxoDeCaixa.Core.Interfaces;

namespace FluxoDeCaixa.Application.UseCases.Lancamentos.Commands
{
    public record CriarLancamentoRequest(TipoLancamento Tipo, string Conta, decimal Valor);

    public class CriarLancamentoCommand
    {
        private readonly ILancamentoRepository _repository;

        public CriarLancamentoCommand(ILancamentoRepository repository)
        {
            _repository = repository;
        }

        public async Task ExecutarAsync(CriarLancamentoRequest request)
        {
            var lancamento = new Lancamento(request.Tipo, request.Conta, request.Valor);
            await _repository.AdicionarAsync(lancamento);
            
            // Aqui poderíamos publicar um evento no Message Broker
            // await _messageBus.PublishAsync(new LancamentoCriadoEvent(lancamento));
        }
    }
}
