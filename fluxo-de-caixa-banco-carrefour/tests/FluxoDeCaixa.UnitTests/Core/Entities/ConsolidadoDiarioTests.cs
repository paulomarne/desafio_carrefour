using System;
using FluxoDeCaixa.Core.Entities;
using FluentAssertions;
using Xunit;

namespace FluxoDeCaixa.UnitTests.Core.Entities
{
    public class ConsolidadoDiarioTests
    {
        [Fact]
        public void Deve_Criar_Consolidado_Com_Saldo_Zero()
        {
            var consolidado = new ConsolidadoDiario(DateTime.UtcNow.Date, "12345");

            consolidado.TotalCreditos.Should().Be(0);
            consolidado.TotalDebitos.Should().Be(0);
            consolidado.Saldo.Should().Be(0);
        }

        [Fact]
        public void Deve_Aplicar_Lancamento_Credito_E_Aumentar_Saldo()
        {
            var consolidado = new ConsolidadoDiario(DateTime.UtcNow.Date, "12345");
            var lancamento = Lancamento.Criar(TipoLancamento.Credito, "12345", 500m, "Venda");

            consolidado.AplicarLancamento(lancamento);

            consolidado.TotalCreditos.Should().Be(500m);
            consolidado.TotalDebitos.Should().Be(0m);
            consolidado.Saldo.Should().Be(500m);
        }

        [Fact]
        public void Deve_Aplicar_Lancamento_Debito_E_Reduzir_Saldo()
        {
            var consolidado = new ConsolidadoDiario(DateTime.UtcNow.Date, "12345");
            var lancamento = Lancamento.Criar(TipoLancamento.Debito, "12345", 200m, "Aluguel");

            consolidado.AplicarLancamento(lancamento);

            consolidado.TotalDebitos.Should().Be(200m);
            consolidado.TotalCreditos.Should().Be(0m);
            consolidado.Saldo.Should().Be(-200m);
        }

        [Fact]
        public void Deve_Calcular_Saldo_Liquido_Corretamente()
        {
            var hoje = DateTime.UtcNow.Date;
            var consolidado = new ConsolidadoDiario(hoje, "12345");

            consolidado.AplicarLancamento(Lancamento.Criar(TipoLancamento.Credito, "12345", 1500m, "Venda A"));
            consolidado.AplicarLancamento(Lancamento.Criar(TipoLancamento.Credito, "12345", 800m, "Venda B"));
            consolidado.AplicarLancamento(Lancamento.Criar(TipoLancamento.Debito, "12345", 300m, "Aluguel"));
            consolidado.AplicarLancamento(Lancamento.Criar(TipoLancamento.Debito, "12345", 150m, "Energia"));

            consolidado.TotalCreditos.Should().Be(2300m);
            consolidado.TotalDebitos.Should().Be(450m);
            consolidado.Saldo.Should().Be(1850m);
        }

        [Fact]
        public void Deve_Rejeitar_Lancamento_De_Conta_Diferente()
        {
            var consolidado = new ConsolidadoDiario(DateTime.UtcNow.Date, "CONTA-A");
            var lancamento = Lancamento.Criar(TipoLancamento.Credito, "CONTA-B", 100m, "Outra conta");

            Action act = () => consolidado.AplicarLancamento(lancamento);

            act.Should().Throw<ArgumentException>().WithMessage("*conta*");
        }

        [Fact]
        public void Deve_Rejeitar_Lancamento_De_Data_Diferente()
        {
            var consolidado = new ConsolidadoDiario(DateTime.UtcNow.Date, "12345");
            var lancamento = Lancamento.Criar(TipoLancamento.Credito, "12345", 100m, "Ontem", DateTime.UtcNow.Date.AddDays(-1));

            Action act = () => consolidado.AplicarLancamento(lancamento);

            act.Should().Throw<ArgumentException>().WithMessage("*data*");
        }

        [Fact]
        public void Deve_Rejeitar_Conta_Vazia_Ao_Criar_Consolidado()
        {
            Action act = () => new ConsolidadoDiario(DateTime.UtcNow.Date, "");

            act.Should().Throw<ArgumentException>().WithMessage("*conta*");
        }

        [Fact]
        public void Deve_Acumular_Multiplos_Lancamentos_Do_Mesmo_Tipo()
        {
            var hoje = DateTime.UtcNow.Date;
            var consolidado = new ConsolidadoDiario(hoje, "12345");

            for (int i = 1; i <= 5; i++)
                consolidado.AplicarLancamento(Lancamento.Criar(TipoLancamento.Credito, "12345", 100m * i, $"Venda {i}"));

            consolidado.TotalCreditos.Should().Be(1500m); // 100+200+300+400+500
        }
    }
}
