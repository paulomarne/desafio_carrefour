using System.Threading.Tasks;
using FluxoDeCaixa.Application.UseCases.Lancamentos.Commands;
using FluxoDeCaixa.Core.Entities;
using FluxoDeCaixa.Core.Interfaces;
using Moq;
using Xunit;

namespace FluxoDeCaixa.UnitTests.Application.UseCases.Lancamentos.Commands
{
    public class CriarLancamentoCommandTests
    {
        [Fact]
        public async Task Deve_Executar_Comando_Com_Sucesso()
        {
            // Arrange
            var repositoryMock = new Mock<ILancamentoRepository>();
            var command = new CriarLancamentoCommand(repositoryMock.Object);
            var request = new CriarLancamentoRequest(TipoLancamento.Credito, "123", 100m);

            // Act
            await command.ExecutarAsync(request);

            // Assert
            repositoryMock.Verify(r => r.AdicionarAsync(It.IsAny<Lancamento>()), Times.Once);
        }
    }
}
