using System;
using System.Threading;
using System.Threading.Tasks;
using FluxoDeCaixa.Application.UseCases.Lancamentos.Commands;
using FluxoDeCaixa.Core.Entities;
using FluxoDeCaixa.Core.Interfaces;
using FluentAssertions;
using Moq;
using Xunit;

namespace FluxoDeCaixa.UnitTests.Application.UseCases.Lancamentos.Commands
{
    public class CriarLancamentoCommandTests
    {
        private readonly Mock<ILancamentoRepository> _repositoryMock = new();
        private readonly Mock<IMessageBus> _messageBusMock = new();
        private readonly CriarLancamentoCommand _command;

        public CriarLancamentoCommandTests()
        {
            _command = new CriarLancamentoCommand(_repositoryMock.Object, _messageBusMock.Object);
        }

        [Fact]
        public async Task Deve_Criar_Lancamento_E_Retornar_Id()
        {
            var request = new CriarLancamentoRequest(TipoLancamento.Credito, "123", 100m, "Venda", DateTime.UtcNow.Date);

            var resultado = await _command.ExecutarAsync(request);

            resultado.Id.Should().NotBeEmpty();
            resultado.DataLancamento.Should().Be(DateTime.UtcNow.Date);
        }

        [Fact]
        public async Task Deve_Persistir_Lancamento_No_Repositorio()
        {
            var request = new CriarLancamentoRequest(TipoLancamento.Debito, "123", 50m, "Aluguel", DateTime.UtcNow.Date);

            await _command.ExecutarAsync(request);

            _repositoryMock.Verify(r => r.AdicionarAsync(
                It.IsAny<Lancamento>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Deve_Publicar_Evento_Apos_Criar_Lancamento()
        {
            var request = new CriarLancamentoRequest(TipoLancamento.Credito, "123", 200m, "PIX recebido", DateTime.UtcNow.Date);

            await _command.ExecutarAsync(request);

            _messageBusMock.Verify(m => m.PublishAsync(
                It.IsAny<object>(),
                "lancamentos",
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Deve_Lancar_Excecao_Para_Valor_Zero()
        {
            var request = new CriarLancamentoRequest(TipoLancamento.Credito, "123", 0m, "Inválido", DateTime.UtcNow.Date);

            Func<Task> act = async () => await _command.ExecutarAsync(request);

            await act.Should().ThrowAsync<ArgumentException>().WithMessage("*valor*");
        }

        [Fact]
        public async Task Deve_Lancar_Excecao_Para_Data_Futura()
        {
            var request = new CriarLancamentoRequest(TipoLancamento.Credito, "123", 100m, "Futuro", DateTime.UtcNow.Date.AddDays(1));

            Func<Task> act = async () => await _command.ExecutarAsync(request);

            await act.Should().ThrowAsync<ArgumentException>().WithMessage("*futura*");
        }

        [Fact]
        public async Task Deve_Nao_Publicar_Evento_Se_Persistencia_Falhar()
        {
            _repositoryMock
                .Setup(r => r.AdicionarAsync(It.IsAny<Lancamento>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Falha de banco"));

            var request = new CriarLancamentoRequest(TipoLancamento.Credito, "123", 100m, "Teste", DateTime.UtcNow.Date);

            Func<Task> act = async () => await _command.ExecutarAsync(request);

            await act.Should().ThrowAsync<Exception>();
            _messageBusMock.Verify(m => m.PublishAsync(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    public class CancelarLancamentoCommandTests
    {
        private readonly Mock<ILancamentoRepository> _repositoryMock = new();
        private readonly Mock<IMessageBus> _messageBusMock = new();
        private readonly CancelarLancamentoCommand _command;

        public CancelarLancamentoCommandTests()
        {
            _command = new CancelarLancamentoCommand(_repositoryMock.Object, _messageBusMock.Object);
        }

        [Fact]
        public async Task Deve_Cancelar_Lancamento_Existente()
        {
            var lancamento = Lancamento.Criar(TipoLancamento.Credito, "123", 100m, "Venda");
            _repositoryMock
                .Setup(r => r.ObterPorIdAsync(lancamento.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(lancamento);

            await _command.ExecutarAsync(lancamento.Id);

            _repositoryMock.Verify(r => r.AtualizarAsync(
                It.Is<Lancamento>(l => l.Status == StatusLancamento.Cancelado),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Deve_Lancar_Excecao_Para_Lancamento_Inexistente()
        {
            _repositoryMock
                .Setup(r => r.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Lancamento?)null);

            Func<Task> act = async () => await _command.ExecutarAsync(Guid.NewGuid());

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*não encontrado*");
        }

        [Fact]
        public async Task Deve_Publicar_Evento_Apos_Cancelamento()
        {
            var lancamento = Lancamento.Criar(TipoLancamento.Credito, "123", 100m, "Venda");
            _repositoryMock
                .Setup(r => r.ObterPorIdAsync(lancamento.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(lancamento);

            await _command.ExecutarAsync(lancamento.Id);

            _messageBusMock.Verify(m => m.PublishAsync(
                It.IsAny<object>(),
                "lancamentos-cancelados",
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
