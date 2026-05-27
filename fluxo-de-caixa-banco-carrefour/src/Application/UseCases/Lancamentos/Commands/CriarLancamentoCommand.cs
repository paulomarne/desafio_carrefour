using System;
using System.Threading;
using System.Threading.Tasks;
using FluxoDeCaixa.Core.Entities;
using FluxoDeCaixa.Core.Events;
using FluxoDeCaixa.Core.Interfaces;

namespace FluxoDeCaixa.Application.UseCases.Lancamentos.Commands
{
    public record CriarLancamentoRequest(
        TipoLancamento Tipo,
        string Conta,
        decimal Valor,
        string Descricao,
        DateTime? DataLancamento = null);

    public record CriarLancamentoResult(Guid Id, DateTime DataLancamento);

    public class CriarLancamentoCommand
    {
        private readonly ILancamentoRepository _repository;
        private readonly IMessageBus _messageBus;

        public CriarLancamentoCommand(ILancamentoRepository repository, IMessageBus messageBus)
        {
            _repository = repository;
            _messageBus = messageBus;
        }

        public async Task<CriarLancamentoResult> ExecutarAsync(CriarLancamentoRequest request, CancellationToken cancellationToken = default)
        {
            var lancamento = Lancamento.Criar(request.Tipo, request.Conta, request.Valor, request.Descricao, request.DataLancamento);

            await _repository.AdicionarAsync(lancamento, cancellationToken);

            // Publicação desacoplada: em produção substituir pelo Outbox Pattern
            // (INSERT lancamento + INSERT outbox_message na mesma transação)
            foreach (var domainEvent in lancamento.DomainEvents)
            {
                if (domainEvent is LancamentoRegistradoDomainEvent registrado)
                {
                    var evento = new LancamentoRegistradoEvent(
                        registrado.LancamentoId, registrado.Tipo, registrado.Conta,
                        registrado.Valor, registrado.Descricao, registrado.DataLancamento);
                    await _messageBus.PublishAsync(evento, "lancamentos", cancellationToken);
                }
            }

            lancamento.ClearDomainEvents();
            return new CriarLancamentoResult(lancamento.Id, lancamento.DataLancamento);
        }
    }

    public class CancelarLancamentoCommand
    {
        private readonly ILancamentoRepository _repository;
        private readonly IMessageBus _messageBus;

        public CancelarLancamentoCommand(ILancamentoRepository repository, IMessageBus messageBus)
        {
            _repository = repository;
            _messageBus = messageBus;
        }

        public async Task ExecutarAsync(Guid lancamentoId, CancellationToken cancellationToken = default)
        {
            var lancamento = await _repository.ObterPorIdAsync(lancamentoId, cancellationToken)
                ?? throw new InvalidOperationException($"Lançamento {lancamentoId} não encontrado.");

            lancamento.Cancelar();
            await _repository.AtualizarAsync(lancamento, cancellationToken);

            foreach (var domainEvent in lancamento.DomainEvents)
            {
                if (domainEvent is LancamentoCanceladoDomainEvent cancelado)
                {
                    var evento = new LancamentoCanceladoEvent(
                        cancelado.LancamentoId, cancelado.Tipo, cancelado.Conta,
                        cancelado.Valor, cancelado.DataLancamento);
                    await _messageBus.PublishAsync(evento, "lancamentos-cancelados", cancellationToken);
                }
            }

            lancamento.ClearDomainEvents();
        }
    }
}
