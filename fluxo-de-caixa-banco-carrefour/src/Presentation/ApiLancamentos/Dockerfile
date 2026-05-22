FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/Presentation/ApiLancamentos/FluxoDeCaixa.ApiLancamentos.csproj", "src/Presentation/ApiLancamentos/"]
COPY ["src/Application/FluxoDeCaixa.Application.csproj", "src/Application/"]
COPY ["src/Core/FluxoDeCaixa.Core.csproj", "src/Core/"]
COPY ["src/Infrastructure/FluxoDeCaixa.Infrastructure.csproj", "src/Infrastructure/"]
RUN dotnet restore "src/Presentation/ApiLancamentos/FluxoDeCaixa.ApiLancamentos.csproj"
COPY .
WORKDIR "/src/src/Presentation/ApiLancamentos"
RUN dotnet build "FluxoDeCaixa.ApiLancamentos.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "FluxoDeCaixa.ApiLancamentos.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FluxoDeCaixa.ApiLancamentos.dll"]
