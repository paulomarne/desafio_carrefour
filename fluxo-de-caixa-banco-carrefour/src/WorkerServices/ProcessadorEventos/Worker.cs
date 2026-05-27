using FluxoDeCaixa.Core.Entities;
using FluxoDeCaixa.Core.Events;
using FluxoDeCaixa.Core.Interfaces;

namespace FluxoDeCaixa.ProcessadorEventos;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IMessageBus _messageBus;
    private readonly IConsolidadoRepository _consolidadoRepository;
    private readonly IConsolidadoCache _consolidadoCache;

    public Worker(
        ILogger<Worker> logger,
        IMessageBus messageBus,
        IConsolidadoRepository consolidadoRepository,
        IConsolidadoCache consolidadoCache)
    {
        _logger = logger;
        _messageBus = messageBus;
        _consolidadoRepository = consolidadoRepository;
        _consolidadoCache = consolidadoCache;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _messageBus.SubscribeAsync<LancamentoRegistradoEvent>(
            "lancamentos", HandleLancamentoRegistradoAsync, stoppingToken);

        await _messageBus.SubscribeAsync<LancamentoCanceladoEvent>(
            "lancamentos-cancelados", HandleLancamentoCanceladoAsync, stoppingToken);

        _logger.LogInformation("Processador de eventos iniciado e aguardando mensagens.");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }

    private async Task HandleLancamentoRegistradoAsync(LancamentoRegistradoEvent evento)
    {
        _logger.LogInformation("Recebido evento de lançamento para conta {Conta} em {Data}",
            evento.Conta, evento.DataLancamento);

        var consolidado = await _consolidadoRepository.ObterPorDataAsync(evento.DataLancamento, evento.Conta)
            ?? new ConsolidadoDiario(evento.DataLancamento, evento.Conta);

        consolidado.AplicarLancamento(
            Lancamento.Criar(evento.Tipo, evento.Conta, evento.Valor, evento.Descricao, evento.DataLancamento));

        await _consolidadoRepository.SalvarAsync(consolidado);
        await _consolidadoCache.InvalidateAsync(evento.DataLancamento, evento.Conta);

        _logger.LogInformation("Consolidado atualizado para {Conta} em {Data}. Saldo atual: {Saldo}",
            consolidado.Conta, consolidado.Data, consolidado.Saldo);
    }

    private async Task HandleLancamentoCanceladoAsync(LancamentoCanceladoEvent evento)
    {
        _logger.LogInformation("Recebido evento de cancelamento para conta {Conta} em {Data}",
            evento.Conta, evento.DataLancamento);

        var consolidado = await _consolidadoRepository.ObterPorDataAsync(evento.DataLancamento, evento.Conta);
        if (consolidado is null)
        {
            _logger.LogWarning("Consolidado não encontrado para estorno. Conta {Conta} Data {Data}",
                evento.Conta, evento.DataLancamento);
            return;
        }

        consolidado.EstornarLancamento(evento.Tipo, evento.Valor);
        await _consolidadoRepository.SalvarAsync(consolidado);
        await _consolidadoCache.InvalidateAsync(evento.DataLancamento, evento.Conta);

        _logger.LogInformation("Estorno aplicado para {Conta} em {Data}. Novo saldo: {Saldo}",
            consolidado.Conta, consolidado.Data, consolidado.Saldo);
    }
}
