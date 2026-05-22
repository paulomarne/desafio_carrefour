FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/WorkerServices/ProcessadorEventos/FluxoDeCaixa.ProcessadorEventos.csproj", "src/WorkerServices/ProcessadorEventos/"]
COPY ["src/Application/FluxoDeCaixa.Application.csproj", "src/Application/"]
COPY ["src/Core/FluxoDeCaixa.Core.csproj", "src/Core/"]
COPY ["src/Infrastructure/FluxoDeCaixa.Infrastructure.csproj", "src/Infrastructure/"]
RUN dotnet restore "src/WorkerServices/ProcessadorEventos/FluxoDeCaixa.ProcessadorEventos.csproj"
COPY .
WORKDIR "/src/src/WorkerServices/ProcessadorEventos"
RUN dotnet build "FluxoDeCaixa.ProcessadorEventos.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "FluxoDeCaixa.ProcessadorEventos.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FluxoDeCaixa.ProcessadorEventos.dll"]
