# Desafio Arquiteto de Soluções - Banco Carrefour: Sistema de Fluxo de Caixa

Este repositório contém a solução proposta para o desafio técnico de Arquiteto de Soluções do Banco Carrefour, focado em um sistema de controle de fluxo de caixa diário.

## Visão Geral da Solução

A solução foi projetada com base em princípios de **Clean Architecture**, **Domain-Driven Design (DDD)**, **CQRS (Command Query Responsibility Segregation)** e **Event-Driven Architecture**. O objetivo é atender aos requisitos de negócio e não funcionais, como alta disponibilidade, escalabilidade e resiliência, utilizando as tecnologias mais recentes do ecossistema .NET.

O sistema é composto por dois serviços principais:

1.  **API de Lançamentos**: Responsável por registrar operações de débito e crédito.
2.  **API de Consolidado Diário**: Responsável por fornecer relatórios do saldo diário consolidado.

Para mais detalhes sobre a arquitetura, justificativas de decisões e estrutura do projeto, consulte o documento [Arquitetura da Solução](docs/solution_architecture.md).

## Tecnologias Utilizadas

-   **Backend**: C# e .NET 8
-   **APIs**: ASP.NET Core Web API
-   **Padrões**: Clean Architecture, DDD, CQRS, Event-Driven
-   **Testes**: xUnit, Moq, FluentAssertions
-   **Controle de Versão**: Git
-   **Containerização**: Docker

## Como Rodar Localmente

Para executar a aplicação localmente, você precisará ter o Docker e o Docker Compose instalados em sua máquina.

1.  **Clone o Repositório**:
    ```bash
    git clone <URL_DO_REPOSITORIO>
    cd fluxo-de-caixa-banco-carrefour
    ```

2.  **Construa e Inicie os Contêineres**:
    A solução inclui um arquivo `docker-compose.yml` (a ser criado) que orquestrará os serviços. Execute o seguinte comando na raiz do projeto:
    ```bash
    docker compose up --build
    ```
    Este comando irá construir as imagens Docker das APIs e do Processador de Eventos, e iniciará todos os serviços definidos no `docker-compose.yml`.

3.  **Acesse as APIs**:
    -   **API de Lançamentos**: `https://localhost:5001/swagger` (ou a porta configurada no docker-compose)
    -   **API de Consolidado Diário**: `https://localhost:5002/swagger` (ou a porta configurada no docker-compose)

## Estrutura do Projeto

```
├── src/
│   ├── Core/ (Domain Layer)
│   ├── Application/ (Application Layer)
│   ├── Infrastructure/ (Infrastructure Layer)
│   ├── Presentation/ (APIs)
│   │   ├── ApiLancamentos/
│   │   └── ApiConsolidadoDiario/
│   └── WorkerServices/
│       └── ProcessadorEventos/
├── tests/
│   └── FluxoDeCaixa.UnitTests/
├── docs/
│   ├── C4_Diagrams/
│   └── solution_architecture.md
├── .gitignore
├── .editorconfig
└── docker-compose.yml
```

## Documentação Adicional

-   [**Arquitetura da Solução**](docs/solution_architecture.md): Detalhes sobre o design arquitetural, justificativas técnicas, requisitos funcionais e não funcionais, e considerações sobre requisitos diferenciais e evoluções futuras.
-   **Diagramas C4**: Os diagramas de Contexto e Contêineres estão disponíveis na pasta `docs/C4_Diagrams/` e incorporados no documento de arquitetura.

## Testes

Para executar os testes unitários, navegue até a raiz do projeto e execute:

```bash
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$PATH:$HOME/.dotnet:$HOME/.dotnet/tools
dotnet test
```

## Próximos Passos e Evoluções Futuras

Conforme detalhado no documento de arquitetura, a solução pode ser expandida com:

-   Implementação completa de Event Sourcing.
-   Padrões de compensação para transações distribuídas (Saga Pattern).
-   Desenvolvimento de uma interface de usuário (UI).
-   Integração com Machine Learning para previsão de fluxo de caixa.
-   Integração com sistemas bancários externos.

---

**Autor**: Manus AI
**Data**: 18 de Maio de 2026
