# Desafio Arquiteto de Soluções — Banco Carrefour: Sistema de Fluxo de Caixa

> **Candidato:** Paulo Marne
> **Data:** Maio de 2026
> **Repositório:** [fluxo-de-caixa-banco-carrefour](fluxo-de-caixa-banco-carrefour/)

---

## Visão Geral da Solução

A solução foi projetada com base em **Clean Architecture**, **Domain-Driven Design (DDD)**, **CQRS** e **Event-Driven Architecture** para atender a dois requisitos não-funcionais críticos que ditam toda a arquitetura:

| Requisito | Solução |
|---|---|
| Lançamentos **não podem cair** se o consolidado cair | Microsserviços independentes + mensageria assíncrona |
| Consolidado suporta **50 rps** com máx. 5% de perda | Cache Redis TTL 30s + projeção pré-computada |

---

## Contexto de Sistema

```mermaid
C4Context
    title Sistema Fluxo de Caixa — Contexto

    Person(comerciante, "Comerciante", "Registra vendas, pagamentos e consulta caixa")
    System(fluxo, "Sistema Fluxo de Caixa", "Dois microsserviços: Lançamentos e Consolidado Diário")
    System_Ext(idp, "Identity Provider", "Autenticação JWT")
    System_Ext(obs, "Observabilidade", "Grafana · Jaeger · Prometheus")

    Rel(comerciante, fluxo, "HTTPS/REST")
    Rel(fluxo, idp, "Valida tokens")
    Rel(fluxo, obs, "Traces e métricas")
```

---

## Arquitetura de Contêineres

```mermaid
C4Container
    title Contêineres do Sistema

    Person(u, "Comerciante")
    Container(gw, "API Gateway", "NGINX", "JWT · Rate Limit · TLS 1.3")
    Container(la, "lancamentos-api", ".NET 9 · :5001", "CQRS · DDD · Outbox")
    Container(ca, "consolidado-api", ".NET 9 · :5002", "Queries · Cache-Aside")
    Container(wk, "worker", ".NET Worker", "Consome eventos · Atualiza consolidado")
    ContainerDb(pg1, "PostgreSQL lancamentos", "PostgreSQL 16", "lancamentos + outbox_messages")
    ContainerDb(pg2, "PostgreSQL consolidado", "PostgreSQL 16", "Read model")
    ContainerDb(rd, "Redis", "Redis 7", "Cache TTL 30s")
    ContainerDb(rb, "RabbitMQ", "RabbitMQ 3", "Fila de eventos")

    Rel(u, gw, "HTTPS")
    Rel(gw, la, "HTTP/mTLS")
    Rel(gw, ca, "HTTP/mTLS")
    Rel(la, pg1, "SQL")
    Rel(la, rb, "Publish")
    Rel(wk, rb, "Subscribe")
    Rel(wk, pg2, "Upsert")
    Rel(wk, rd, "Invalidate")
    Rel(ca, rd, "Cache GET")
    Rel(ca, pg2, "Fallback SQL")
```

---

## Clean Architecture — Camadas

```mermaid
graph TB
    P["Presentation Layer\nControllers · Middleware · Health Checks"]
    A["Application Layer\nCommands · Queries · Handlers · DTOs"]
    D["Domain Layer (Core)\nEntidades · Domain Events · Interfaces\nZERO dependências externas"]
    I["Infrastructure Layer\nEF Core · Redis · RabbitMQ · Retry"]

    P --> A --> D
    I --> D

    style D fill:#2f9e44,color:#fff
    style A fill:#1971c2,color:#fff
    style P fill:#7048e8,color:#fff
    style I fill:#e67700,color:#fff
```

---

## Fluxo de Registro de Lançamento

```mermaid
sequenceDiagram
    actor C as Comerciante
    participant LA as lancamentos-api
    participant DB as PostgreSQL
    participant OB as Outbox
    participant WK as Worker
    participant MB as RabbitMQ
    participant CA as consolidado-api
    participant RD as Redis

    C->>LA: POST /api/lancamentos
    LA->>LA: Lancamento.Criar() — valida invariantes
    LA->>DB: BEGIN TRANSACTION
    LA->>DB: INSERT lancamentos
    LA->>OB: INSERT outbox_messages
    LA->>DB: COMMIT
    LA-->>C: 201 Created {id}

    Note over WK,MB: ~2s depois
    WK->>OB: Lê pendentes
    WK->>MB: Publish evento
    MB->>CA: Deliver
    CA->>CA: AplicarLancamento()
    CA->>DB: UPDATE consolidado
    CA->>RD: INVALIDATE cache
```

---

## Requisitos Não Funcionais

### Escalabilidade

```mermaid
graph LR
    GW[API Gateway] --> LA_POOL["lancamentos-api\nHPA 2-5 pods"]
    GW --> CA_POOL["consolidado-api\nHPA 3-10 pods"]
    CA_POOL --> RD[(Redis\nShared Cache)]
    LA_POOL --> MB[(RabbitMQ)]
    MB --> WK_POOL["worker\nHPA 1-3 pods"]
```

### Cache-Aside para 50 rps

```mermaid
flowchart LR
    REQ([GET /consolidado]) --> C{Redis?}
    C -- HIT\n<5ms --> OK([200 OK])
    C -- MISS\n~55ms --> PG[(PostgreSQL)]
    PG --> STORE[SET Redis TTL 30s] --> OK
```

### Tolerância a Falhas

```mermaid
stateDiagram-v2
    [*] --> Fechado
    Fechado --> Aberto : 5 falhas consecutivas
    Aberto --> MeioAberto : Após 30s
    MeioAberto --> Fechado : Teste OK
    MeioAberto --> Aberto : Teste falhou
```

---

## Testes e Qualidade

| Suite | Casos | Cobertura |
|---|---|---|
| Domínio — `LancamentoTests` | 14 | Invariantes, Domain Events, cancelamento, edge cases |
| Domínio — `ConsolidadoDiarioTests` | 8 | Saldo, acumulação, contas diferentes, datas |
| Application — `CriarLancamentoCommandTests` | 6 | Handler, repo, evento, falhas |
| Application — `CancelarLancamentoCommandTests` | 3 | Cancelamento, inexistente, evento |
| Application — `ObterConsolidadoDiarioQueryTests` | 5 | Cache HIT/MISS, zeros, saldo correto |
| **Total** | **36** | |

```bash
dotnet test --collect:"XPlat Code Coverage"
```

---

## Tecnologias Utilizadas

| Categoria | Tecnologia | Justificativa |
|---|---|---|
| **Runtime** | .NET 9 / C# | ~15% menos alocações HTTP vs .NET 8, melhor startup em containers |
| **APIs** | ASP.NET Core Minimal API | Menor overhead de reflection vs MVC Controller |
| **Mensageria** | RabbitMQ → Azure Service Bus | Desacoplamento assíncrono entre microsserviços |
| **Cache** | IMemoryCache → Redis 7 | 50 rps = 180k queries/h sem cache; Redis suporta >10k rps |
| **Banco** | In-Memory → PostgreSQL 16 | ACID em produção, sem lock contention com CQRS |
| **Testes** | xUnit + FluentAssertions + Moq | Legibilidade e mocking preciso de contratos |
| **Containers** | Docker Compose → AKS | Portabilidade e escalonamento horizontal |
| **Observabilidade** | OpenTelemetry + Grafana + Jaeger | Traces distribuídos end-to-end |

---

## Como Rodar Localmente

```bash
# Clone
git clone <url>
cd fluxo-de-caixa-banco-carrefour

# Docker Compose (recomendado — stack completa)
docker compose up --build

# Acesse:
# http://localhost:5001/swagger  → API Lançamentos
# http://localhost:5002/swagger  → API Consolidado
# http://localhost:3000          → Grafana
# http://localhost:16686         → Jaeger

# Apenas .NET (sem Docker)
dotnet run --project src/Presentation/ApiLancamentos &
dotnet run --project src/Presentation/ApiConsolidado &
dotnet run --project src/WorkerServices/ProcessadorEventos

# Testes
dotnet test
```

---

## Próximos Passos e Evoluções Futuras

```mermaid
graph LR
    subgraph CP["Curto Prazo"]
        A[Event Sourcing\nEventStoreDB]
        B[Pact.NET\nContract Testing]
        C[GitHub Actions\nCI/CD]
        D[Kubernetes\nHelm Charts]
    end

    subgraph MP["Médio Prazo"]
        E[Saga Pattern\nTransações Distribuídas]
        F[Multi-Tenancy\nMúltiplas contas]
        G[Read Replica\nPostgreSQL]
    end

    subgraph LP["Longo Prazo"]
        H[ML Previsão\nFluxo de Caixa]
        I[Conciliação Bancária\nImportação OFX]
        J[Chaos Engineering\nSimmy]
    end

    CP --> MP --> LP
```

---

**Documentação detalhada:** [solution_architecture.md](fluxo-de-caixa-banco-carrefour/docs/solution_architecture.md)

**Autor:** Paulo Marne · Maio de 2026
