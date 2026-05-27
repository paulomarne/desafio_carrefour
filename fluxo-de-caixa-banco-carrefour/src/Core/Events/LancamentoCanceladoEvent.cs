using System;
using FluxoDeCaixa.Core.Entities;

namespace FluxoDeCaixa.Core.Events
{
    public record LancamentoCanceladoEvent(
        Guid Id,
        TipoLancamento Tipo,
        string Conta,
        decimal Valor,
        DateTime DataLancamento
    );
}
