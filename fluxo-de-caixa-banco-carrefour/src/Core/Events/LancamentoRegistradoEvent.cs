using System;
using FluxoDeCaixa.Core.Entities;

namespace FluxoDeCaixa.Core.Events
{
    public record LancamentoRegistradoEvent(
        Guid Id,
        TipoLancamento Tipo,
        string Conta,
        decimal Valor,
        string Descricao,
        DateTime DataLancamento
    );
}
