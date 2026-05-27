using System;
using System.Threading;
using System.Threading.Tasks;
using FluxoDeCaixa.Application.UseCases.ConsolidadoDiario.Queries;
using FluxoDeCaixa.Core.Interfaces;
using FluentAssertions;
using Moq;
using Xunit;

// Alias para evitar conflito de nome entre namespace do teste e a entidade
using ConsolidadoEntity = FluxoDeCaixa.Core.Entities.ConsolidadoDiario;
using LancamentoEntity = FluxoDeCaixa.Core.Entities.Lancamento;
using TipoEnum = FluxoDeCaixa.Core.Entities.TipoLancamento;

namespace FluxoDeCaixa.UnitTests.Application.UseCases.ConsolidadoDiarioTests
{
    public class ObterConsolidadoDiarioQueryTests
    {
        private readonly Mock<IConsolidadoCache> _cacheMock = new();
        private readonly Mock<IConsolidadoRepository> _repositoryMock = new();
        private readonly ObterConsolidadoDiarioQuery _query;

        public ObterConsolidadoDiarioQueryTests()
        {
            _query = new ObterConsolidadoDiarioQuery(_cacheMock.Object, _repositoryMock.Object);
        }

        [Fact]
        public async Task Deve_Retornar_Do_Cache_Quando_Cache_Hit()
        {
            var hoje = DateTime.UtcNow.Date;
            var consolidadoEmCache = new ConsolidadoEntity(hoje, "12345");
            consolidadoEmCache.AplicarLancamento(LancamentoEntity.Criar(TipoEnum.Credito, "12345", 500m, "Venda"));

            _cacheMock.Setup(c => c.ObterAsync(hoje, "12345"))
                .ReturnsAsync(consolidadoEmCache);

            var resultado = await _query.ExecutarAsync(hoje, "12345");

            resultado.TotalCreditos.Should().Be(500m);
            _repositoryMock.Verify(r => r.ObterPorDataAsync(It.IsAny<DateTime>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Deve_Buscar_Repositorio_E_Salvar_Cache_Quando_Cache_Miss()
        {
            var hoje = DateTime.UtcNow.Date;
            var consolidadoNoBanco = new ConsolidadoEntity(hoje, "12345");
            consolidadoNoBanco.AplicarLancamento(LancamentoEntity.Criar(TipoEnum.Credito, "12345", 300m, "Recebimento"));

            _cacheMock.Setup(c => c.ObterAsync(hoje, "12345"))
                .ReturnsAsync((ConsolidadoEntity?)null);
            _repositoryMock.Setup(r => r.ObterPorDataAsync(hoje, "12345"))
                .ReturnsAsync(consolidadoNoBanco);

            var resultado = await _query.ExecutarAsync(hoje, "12345");

            resultado.TotalCreditos.Should().Be(300m);
            _cacheMock.Verify(c => c.SalvarAsync(consolidadoNoBanco), Times.Once);
        }

        [Fact]
        public async Task Deve_Retornar_Zeros_Quando_Dia_Sem_Lancamentos()
        {
            var hoje = DateTime.UtcNow.Date;
            _cacheMock.Setup(c => c.ObterAsync(hoje, "12345"))
                .ReturnsAsync((ConsolidadoEntity?)null);
            _repositoryMock.Setup(r => r.ObterPorDataAsync(hoje, "12345"))
                .ReturnsAsync((ConsolidadoEntity?)null);

            var resultado = await _query.ExecutarAsync(hoje, "12345");

            resultado.TotalCreditos.Should().Be(0);
            resultado.TotalDebitos.Should().Be(0);
            resultado.SaldoLiquido.Should().Be(0);
        }

        [Fact]
        public async Task Deve_Retornar_Saldo_Correto_Creditos_E_Debitos()
        {
            var hoje = DateTime.UtcNow.Date;
            var consolidado = new ConsolidadoEntity(hoje, "12345");
            consolidado.AplicarLancamento(LancamentoEntity.Criar(TipoEnum.Credito, "12345", 1000m, "Venda"));
            consolidado.AplicarLancamento(LancamentoEntity.Criar(TipoEnum.Debito, "12345", 400m, "Despesa"));

            _cacheMock.Setup(c => c.ObterAsync(hoje, "12345"))
                .ReturnsAsync(consolidado);

            var resultado = await _query.ExecutarAsync(hoje, "12345");

            resultado.TotalCreditos.Should().Be(1000m);
            resultado.TotalDebitos.Should().Be(400m);
            resultado.SaldoLiquido.Should().Be(600m);
        }

        [Fact]
        public async Task Deve_Nao_Salvar_Cache_Quando_Repositorio_Retorna_Nulo()
        {
            var hoje = DateTime.UtcNow.Date;
            _cacheMock.Setup(c => c.ObterAsync(hoje, "12345"))
                .ReturnsAsync((ConsolidadoEntity?)null);
            _repositoryMock.Setup(r => r.ObterPorDataAsync(hoje, "12345"))
                .ReturnsAsync((ConsolidadoEntity?)null);

            await _query.ExecutarAsync(hoje, "12345");

            _cacheMock.Verify(c => c.SalvarAsync(It.IsAny<ConsolidadoEntity>()), Times.Never);
        }
    }
}
