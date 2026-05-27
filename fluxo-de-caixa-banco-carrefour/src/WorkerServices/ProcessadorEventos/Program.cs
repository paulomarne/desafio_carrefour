using FluxoDeCaixa.Infrastructure.DependencyInjection;
using FluxoDeCaixa.ProcessadorEventos;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructureServices();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
