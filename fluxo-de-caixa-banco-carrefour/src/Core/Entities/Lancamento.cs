using System;
using System.Collections.Generic;

namespace FluxoDeCaixa.Core.Entities
{
    public enum TipoLancamento
    {
        Debito = 1,
        Credito = 2
    }

    public enum StatusLancamento
    {
        Ativo,
        Cancelado
    }

    public class Lancamento
    {
        private readonly List<IDomainEvent> _domainEvents = new();

        public Guid Id { get; private set; }
        public TipoLancamento Tipo { get; private set; }
        public string Conta { get; private set; }
        public decimal Valor { get; private set; }
        public string Descricao { get; private set; }
        public DateTime DataLancamento { get; private set; }
        public DateTime DataCriacao { get; private set; }
        public StatusLancamento Status { get; private set; }
        public DateTime? DataCancelamento { get; private set; }

        public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

        private Lancamento()
        {
            Conta = string.Empty;
            Descricao = string.Empty;
        }

        public static Lancamento Criar(TipoLancamento tipo, string conta, decimal valor, string descricao, DateTime? dataLancamento = null)
        {
            if (string.IsNullOrWhiteSpace(conta))
                throw new ArgumentException("A conta não pode ser vazia.", nameof(conta));

            if (valor <= 0)
                throw new ArgumentException("O valor deve ser maior que zero.", nameof(valor));

            if (valor > 10_000_000)
                throw new ArgumentException("O valor não pode exceder R$ 10.000.000.", nameof(valor));

            if (string.IsNullOrWhiteSpace(descricao))
                throw new ArgumentException("A descrição é obrigatória.", nameof(descricao));

            if (descricao.Length > 255)
                throw new ArgumentException("A descrição não pode ter mais de 255 caracteres.", nameof(descricao));

            var lancamentoDate = dataLancamento?.Date ?? DateTime.UtcNow.Date;
            if (lancamentoDate > DateTime.UtcNow.Date)
                throw new ArgumentException("A data do lançamento não pode ser futura.", nameof(dataLancamento));

            var lancamento = new Lancamento
            {
                Id = Guid.NewGuid(),
                Tipo = tipo,
                Conta = conta,
                Valor = valor,
                Descricao = descricao,
                DataLancamento = lancamentoDate,
                DataCriacao = DateTime.UtcNow,
                Status = StatusLancamento.Ativo
            };

            lancamento._domainEvents.Add(new LancamentoRegistradoDomainEvent(lancamento.Id, tipo, conta, valor, descricao, lancamentoDate));
            return lancamento;
        }

        public void Cancelar()
        {
            if (Status == StatusLancamento.Cancelado)
                throw new InvalidOperationException("Lançamento já está cancelado.");

            Status = StatusLancamento.Cancelado;
            DataCancelamento = DateTime.UtcNow;

            _domainEvents.Add(new LancamentoCanceladoDomainEvent(Id, Tipo, Conta, Valor, DataLancamento));
        }

        public void ClearDomainEvents() => _domainEvents.Clear();

        public bool EstaAtivo() => Status == StatusLancamento.Ativo;
    }

    public interface IDomainEvent
    {
        DateTime OcorridoEm { get; }
    }

    public record LancamentoRegistradoDomainEvent(
        Guid LancamentoId, TipoLancamento Tipo, string Conta,
        decimal Valor, string Descricao, DateTime DataLancamento) : IDomainEvent
    {
        public DateTime OcorridoEm { get; } = DateTime.UtcNow;
    }

    public record LancamentoCanceladoDomainEvent(
        Guid LancamentoId, TipoLancamento Tipo, string Conta,
        decimal Valor, DateTime DataLancamento) : IDomainEvent
    {
        public DateTime OcorridoEm { get; } = DateTime.UtcNow;
    }
}
