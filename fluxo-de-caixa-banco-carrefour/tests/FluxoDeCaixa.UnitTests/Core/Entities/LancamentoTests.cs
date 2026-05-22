using System;
using FluxoDeCaixa.Core.Entities;
using FluentAssertions;
using Xunit;

namespace FluxoDeCaixa.UnitTests.Core.Entities
{
    public class LancamentoTests
    {
        [Fact]
        public void Deve_Criar_Lancamento_Com_Sucesso()
        {
            // Arrange
            var tipo = TipoLancamento.Credito;
            var conta = "12345";
            var valor = 100.50m;

            // Act
            var lancamento = new Lancamento(tipo, conta, valor);

            // Assert
            lancamento.Id.Should().NotBeEmpty();
            lancamento.Tipo.Should().Be(tipo);
            lancamento.Conta.Should().Be(conta);
            lancamento.Valor.Should().Be(valor);
            lancamento.DataCriacao.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(null)]
        public void Deve_Lancar_Excecao_Quando_Conta_For_Invalida(string contaInvalida)
        {
            // Arrange
            var tipo = TipoLancamento.Debito;
            var valor = 50m;

            // Act
            Action act = () => new Lancamento(tipo, contaInvalida, valor);

            // Assert
            act.Should().Throw<ArgumentException>().WithMessage("*conta*");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-10)]
        public void Deve_Lancar_Excecao_Quando_Valor_For_Invalido(decimal valorInvalido)
        {
            // Arrange
            var tipo = TipoLancamento.Debito;
            var conta = "12345";

            // Act
            Action act = () => new Lancamento(tipo, conta, valorInvalido);

            // Assert
            act.Should().Throw<ArgumentException>().WithMessage("*valor*");
        }
    }
}
