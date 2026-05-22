using System;

namespace FluxoDeCaixa.Core.Entities
{
    public enum TipoLancamento
    {
        Debito,
        Credito
    }

    public class Lancamento
    {
        public Guid Id { get; private set; }
        public TipoLancamento Tipo { get; private set; }
        public string Conta { get; private set; }
        public decimal Valor { get; private set; }
        public DateTime DataCriacao { get; private set; }

        public Lancamento(TipoLancamento tipo, string conta, decimal valor)
        {
            if (string.IsNullOrWhiteSpace(conta))
                throw new ArgumentException("A conta não pode ser vazia.", nameof(conta));

            if (valor <= 0)
                throw new ArgumentException("O valor deve ser maior que zero.", nameof(valor));

            Id = Guid.NewGuid();
            Tipo = tipo;
            Conta = conta;
            Valor = valor;
            DataCriacao = DateTime.UtcNow;
        }
    }
}
