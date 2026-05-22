using System.Threading.Tasks;
using FluxoDeCaixa.Core.Entities;

namespace FluxoDeCaixa.Core.Interfaces
{
    public interface ILancamentoRepository
    {
        Task AdicionarAsync(Lancamento lancamento);
    }
}
