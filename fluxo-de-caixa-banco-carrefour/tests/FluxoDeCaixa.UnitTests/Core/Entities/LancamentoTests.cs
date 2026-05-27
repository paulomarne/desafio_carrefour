using System;
using FluxoDeCaixa.Core.Entities;
using FluentAssertions;
using Xunit;

namespace FluxoDeCaixa.UnitTests.Core.Entities
{
    public class LancamentoTests
    {
        [Fact]
        public void Deve_Criar_Lancamento_Credito_Com_Sucesso()
        {
            var lancamento = Lancamento.Criar(TipoLancamento.Credito, "12345", 100.50m, "Venda de mercadoria");

            lancamento.Id.Should().NotBeEmpty();
            lancamento.Tipo.Should().Be(TipoLancamento.Credito);
            lancamento.Conta.Should().Be("12345");
            lancamento.Valor.Should().Be(100.50m);
            lancamento.Status.Should().Be(StatusLancamento.Ativo);
            lancamento.DataCriacao.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        }

        [Fact]
        public void Deve_Criar_Lancamento_Debito_Com_Sucesso()
        {
            var lancamento = Lancamento.Criar(TipoLancamento.Debito, "12345", 50m, "Pagamento de fornecedor");

            lancamento.Tipo.Should().Be(TipoLancamento.Debito);
            lancamento.EstaAtivo().Should().BeTrue();
        }

        [Fact]
        public void Deve_Publicar_Evento_De_Dominio_Ao_Criar()
        {
            var lancamento = Lancamento.Criar(TipoLancamento.Credito, "12345", 200m, "PIX");

            lancamento.DomainEvents.Should().ContainSingle(e => e is LancamentoRegistradoDomainEvent);
        }

        [Fact]
        public void Deve_Cancelar_Lancamento_Ativo()
        {
            var lancamento = Lancamento.Criar(TipoLancamento.Credito, "12345", 100m, "Venda");

            lancamento.Cancelar();

            lancamento.Status.Should().Be(StatusLancamento.Cancelado);
            lancamento.DataCancelamento.Should().NotBeNull();
            lancamento.EstaAtivo().Should().BeFalse();
        }

        [Fact]
        public void Deve_Publicar_Evento_De_Cancelamento()
        {
            var lancamento = Lancamento.Criar(TipoLancamento.Credito, "12345", 100m, "Venda");
            lancamento.ClearDomainEvents();

            lancamento.Cancelar();

            lancamento.DomainEvents.Should().ContainSingle(e => e is LancamentoCanceladoDomainEvent);
        }

        [Fact]
        public void Deve_Lancar_Excecao_Ao_Cancelar_Ja_Cancelado()
        {
            var lancamento = Lancamento.Criar(TipoLancamento.Credito, "12345", 100m, "Venda");
            lancamento.Cancelar();

            Action act = () => lancamento.Cancelar();

            act.Should().Throw<InvalidOperationException>().WithMessage("*já está cancelado*");
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(null)]
        public void Deve_Rejeitar_Conta_Invalida(string contaInvalida)
        {
            Action act = () => Lancamento.Criar(TipoLancamento.Debito, contaInvalida, 50m, "Pagamento");

            act.Should().Throw<ArgumentException>().WithMessage("*conta*");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-10)]
        [InlineData(-0.01)]
        public void Deve_Rejeitar_Valor_Invalido(decimal valorInvalido)
        {
            Action act = () => Lancamento.Criar(TipoLancamento.Debito, "12345", valorInvalido, "Estorno");

            act.Should().Throw<ArgumentException>().WithMessage("*valor*");
        }

        [Fact]
        public void Deve_Rejeitar_Valor_Acima_Do_Limite()
        {
            Action act = () => Lancamento.Criar(TipoLancamento.Credito, "12345", 10_000_001m, "Valor alto");

            act.Should().Throw<ArgumentException>().WithMessage("*10.000.000*");
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(null)]
        public void Deve_Rejeitar_Descricao_Vazia(string descricaoInvalida)
        {
            Action act = () => Lancamento.Criar(TipoLancamento.Debito, "12345", 50m, descricaoInvalida);

            act.Should().Throw<ArgumentException>().WithMessage("*descrição*");
        }

        [Fact]
        public void Deve_Rejeitar_Descricao_Muito_Longa()
        {
            var descricaoLonga = new string('x', 256);

            Action act = () => Lancamento.Criar(TipoLancamento.Debito, "12345", 50m, descricaoLonga);

            act.Should().Throw<ArgumentException>().WithMessage("*255*");
        }

        [Fact]
        public void Deve_Rejeitar_Data_Futura()
        {
            Action act = () => Lancamento.Criar(TipoLancamento.Credito, "12345", 100m, "Futuro", DateTime.UtcNow.Date.AddDays(1));

            act.Should().Throw<ArgumentException>().WithMessage("*futura*");
        }

        [Fact]
        public void Deve_Usar_Data_Atual_Quando_Nao_Informada()
        {
            var lancamento = Lancamento.Criar(TipoLancamento.Credito, "12345", 100m, "Hoje");

            lancamento.DataLancamento.Should().Be(DateTime.UtcNow.Date);
        }

        [Fact]
        public void Deve_Limpar_Eventos_De_Dominio()
        {
            var lancamento = Lancamento.Criar(TipoLancamento.Credito, "12345", 100m, "Venda");

            lancamento.ClearDomainEvents();

            lancamento.DomainEvents.Should().BeEmpty();
        }
    }
}
