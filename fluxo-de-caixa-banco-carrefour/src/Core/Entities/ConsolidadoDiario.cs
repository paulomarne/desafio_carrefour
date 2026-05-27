using System;

namespace FluxoDeCaixa.Core.Entities
{
    public class ConsolidadoDiario
    {
        public DateTime Data { get; private set; }
        public string Conta { get; private set; }
        public decimal TotalDebitos { get; private set; }
        public decimal TotalCreditos { get; private set; }
        public decimal Saldo => TotalCreditos - TotalDebitos;

        public ConsolidadoDiario(DateTime data, string conta)
        {
            if (string.IsNullOrWhiteSpace(conta))
                throw new ArgumentException("A conta não pode ser vazia.", nameof(conta));

            Data = data.Date;
            Conta = conta;
        }

        public void AplicarLancamento(Lancamento lancamento)
        {
            if (lancamento.DataLancamento.Date != Data)
                throw new ArgumentException("O lançamento não pertence à data deste consolidado.", nameof(lancamento));

            if (!string.Equals(lancamento.Conta, Conta, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("O lançamento não pertence à conta deste consolidado.", nameof(lancamento));

            if (lancamento.Tipo == TipoLancamento.Debito)
                TotalDebitos += lancamento.Valor;
            else
                TotalCreditos += lancamento.Valor;
        }

        public void EstornarLancamento(TipoLancamento tipo, decimal valor)
        {
            if (tipo == TipoLancamento.Debito)
                TotalDebitos = Math.Max(0, TotalDebitos - valor);
            else
                TotalCreditos = Math.Max(0, TotalCreditos - valor);
        }
    }
}
