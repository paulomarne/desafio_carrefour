FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/Presentation/ApiConsolidado/FluxoDeCaixa.ApiConsolidado.csproj", "src/Presentation/ApiConsolidado/"]
COPY ["src/Application/FluxoDeCaixa.Application.csproj", "src/Application/"]
COPY ["src/Core/FluxoDeCaixa.Core.csproj", "src/Core/"]
COPY ["src/Infrastructure/FluxoDeCaixa.Infrastructure.csproj", "src/Infrastructure/"]
RUN dotnet restore "src/Presentation/ApiConsolidado/FluxoDeCaixa.ApiConsolidado.csproj"
COPY .
WORKDIR "/src/src/Presentation/ApiConsolidado"
RUN dotnet build "FluxoDeCaixa.ApiConsolidado.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "FluxoDeCaixa.ApiConsolidado.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FluxoDeCaixa.ApiConsolidado.dll"]
