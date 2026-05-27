using System.Text.Json.Serialization;
using FluxoDeCaixa.Application.UseCases.ConsolidadoDiario.Queries;
using FluxoDeCaixa.Application.UseCases.Lancamentos.Commands;
using FluxoDeCaixa.Infrastructure.DependencyInjection;
using FluxoDeCaixa.ProcessadorEventos;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructureServices();

// JSON: serializar enums como string ("Ativo", "Credito" em vez de 0, 2)
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
        opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddApplicationPart(typeof(FluxoDeCaixa.ApiConsolidado.Controllers.ConsolidadoController).Assembly);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Fluxo de Caixa PoC", Version = "v1" });
});

// Commands e Queries
builder.Services.AddScoped<CriarLancamentoCommand>();
builder.Services.AddScoped<CancelarLancamentoCommand>();
builder.Services.AddScoped<ObterConsolidadoDiarioQuery>();

// Worker embutido: no PoC, roda no mesmo processo para compartilhar InMemory storage.
// Em produção: processo separado que usa RabbitMQ como broker.
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok("Healthy"));

app.Run();
