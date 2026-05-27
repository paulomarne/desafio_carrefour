# Arquitetura de Soluções — Controle de Fluxo de Caixa

> **Desafio:** Arquiteto de Soluções — Banco Carrefour · 2026
> **Stack:** .NET 9 · PostgreSQL · Redis · RabbitMQ · Docker · CQRS · DDD · Clean Architecture · Event-Driven

---

## TL;DR — Decisões que guiam toda a arquitetura

Os dois requisitos não-funcionais críticos ditam cada escolha arquitetural:

| Requisito | Meta | Solução |
|---|---|---|
| **RNF-01** Lançamentos não caem se o consolidado cair | 100% isolamento | Microsserviços independentes + mensageria assíncrona |
| **RNF-02** Consolidado suporta 50 rps com máx. 5% perda | P99 < 200ms | Cache Redis TTL 30s + projeção pré-computada |
| **RNF-03** Atomicidade entre persistência e evento | Zero perda de dados | Outbox Pattern (mesma transação) |

---

## Índice

1. [Visão AS-IS / TO-BE](#visão-as-is--to-be)
2. [Domínios Funcionais (DDD)](#domínios-funcionais-ddd)
3. [Arquitetura Alvo — C4 Container](#arquitetura-alvo--c4-container)
4. [Camadas Internas — Clean Architecture](#camadas-internas--clean-architecture)
5. [Fluxos de Dados Críticos](#fluxos-de-dados-críticos)
6. [Requisitos Não Funcionais](#requisitos-não-funcionais)
7. [ADRs — Decisões Arquiteturais](#adrs--decisões-arquiteturais)
8. [Estrutura do Projeto](#estrutura-do-projeto)
9. [Observabilidade](#observabilidade)
10. [Segurança](#segurança)
11. [Arquitetura de Transição](#arquitetura-de-transição)
12. [Testes e Qualidade](#testes-e-qualidade)
13. [Kubernetes e Infraestrutura](#kubernetes-e-infraestrutura)
14. [Estimativa de Custos Azure](#estimativa-de-custos-azure)
15. [Evoluções Futuras](#evoluções-futuras)
16. [Como Rodar Localmente](#como-rodar-localmente)

---

## Visão AS-IS / TO-BE

### AS-IS — Situação Atual (Sistema Legado)

```mermaid
flowchart TD
    U([Comerciante]) --> MONO[Monolito\nFluxo de Caixa]
    MONO --> DB[(Banco de Dados\nÚnico)]
    MONO --> REL[Relatórios\nSíncronos]

    style MONO fill:#ff6b6b,color:#fff
    style DB fill:#ffa94d,color:#fff
```

**Problemas identificados:**
- Acoplamento total: falha em qualquer módulo derruba o sistema inteiro
- Relatórios síncronos competem com lançamentos por recursos do banco
- Impossível escalar consolidado independentemente dos lançamentos
- Zero separação de contextos de domínio

### TO-BE — Arquitetura Alvo

```mermaid
flowchart TD
    U([Comerciante]) --> GW[API Gateway\nJWT · Rate Limit · TLS]

    GW --> LA[lancamentos-api\n:5001]
    GW --> CA[consolidado-api\n:5002]

    LA --> PG1[(PostgreSQL\nlancamentos)]
    LA --> MB[(RabbitMQ\nBroker)]

    MB --> WK[Worker\nProcessadorEventos]
    WK --> PG2[(PostgreSQL\nconsolidado)]
    WK --> RD[(Redis\nCache TTL 30s)]

    CA --> RD
    CA --> PG2

    LA -.->|independente| CA

    style LA fill:#339af0,color:#fff
    style CA fill:#51cf66,color:#fff
    style WK fill:#ff922b,color:#fff
    style GW fill:#845ef7,color:#fff
    style RD fill:#f03e3e,color:#fff
```

**Ganhos mensuráveis:**
| Aspecto | AS-IS | TO-BE |
|---|---|---|
| Isolamento de falhas | Nenhum | Total (RNF-01 atendido) |
| Throughput consolidado | ~200 rps (sem cache) | >10.000 rps (cache hit) |
| Latência consolidado | ~50ms | <5ms (cache hit) |
| Escalabilidade | Vertical apenas | Horizontal independente |
| Observabilidade | Logs básicos | Traces distribuídos + métricas |

---

## Domínios Funcionais (DDD)

### Bounded Contexts

```mermaid
graph LR
    subgraph BC1["Bounded Context: Lançamentos (Core Domain)"]
        L1[Lancamento\nEntidade Raiz]
        L2[TipoLancamento\nEnum]
        L3[StatusLancamento\nEnum]
        L4[LancamentoRegistradoEvent\nDomain Event]
        L5[LancamentoCanceladoEvent\nDomain Event]
    end

    subgraph BC2["Bounded Context: Consolidação Diária (Core Domain)"]
        C1[ConsolidadoDiario\nEntidade Raiz]
        C2[TotalCréditos\nValue Object]
        C3[TotalDébitos\nValue Object]
        C4[SaldoLíquido\nComputado]
    end

    subgraph SK["Shared Kernel"]
        SK1[IDomainEvent]
        SK2[IRepository]
    end

    L4 -->|evento integração| C1
    L5 -->|estorno| C1
    L1 -.->|publica| L4
    L1 -.->|publica| L5
```

### Linguagem Ubíqua

| Termo | Definição no Domínio |
|---|---|
| **Lançamento** | Registro financeiro de entrada ou saída na conta do comerciante |
| **Débito** | Saída de caixa — reduz o saldo disponível |
| **Crédito** | Entrada de caixa — aumenta o saldo disponível |
| **Consolidado Diário** | Projeção calculada do saldo total de um dia |
| **Saldo Líquido** | TotalCréditos − TotalDébitos de um dia |
| **Estorno** | Reversão aplicada ao consolidado quando lançamento é cancelado |

### Capacidades de Negócio

| Capacidade | Domínio | Criticidade |
|---|---|---|
| Registrar lançamento (débito/crédito) | Lançamentos | Crítica |
| Cancelar lançamento (soft-delete) | Lançamentos | Alta |
| Consultar lançamentos por data | Lançamentos | Alta |
| Consultar saldo consolidado do dia | Consolidação | Crítica |
| Reprocessar consolidado (admin) | Consolidação | Média |

---

## Arquitetura Alvo — C4 Container

### Nível de Contexto (C4-L1)

```mermaid
C4Context
    title Diagrama de Contexto — Sistema Fluxo de Caixa

    Person(comerciante, "Comerciante", "Registra vendas, pagamentos e consulta o caixa do dia")
    Person(admin, "Administrador", "Monitora saúde do sistema e reprocessa dados")

    System(fluxo, "Sistema Fluxo de Caixa", "Registra lançamentos e consolida saldo diário em tempo real")

    System_Ext(idp, "Identity Provider\n(Azure AD)", "Autenticação e autorização JWT")
    System_Ext(monitor, "Grafana + Jaeger", "Observabilidade e rastreamento distribuído")

    Rel(comerciante, fluxo, "Registra e consulta", "HTTPS/REST")
    Rel(admin, fluxo, "Monitora e administra", "HTTPS/REST")
    Rel(fluxo, idp, "Valida tokens JWT", "HTTPS")
    Rel(fluxo, monitor, "Envia traces e métricas", "OTLP/gRPC")
```

### Nível de Contêiner (C4-L2)

```mermaid
C4Container
    title Diagrama de Contêineres — Fluxo de Caixa

    Person(user, "Comerciante")

    Container(gw, "API Gateway", "NGINX / Azure APIM", "Rate Limiting · JWT · TLS 1.3 · CORS")

    Container(lancamentos, "lancamentos-api", ".NET 9 Minimal API", "Registra e cancela lançamentos\nCQRS · DDD · Outbox Pattern")
    Container(consolidado, "consolidado-api", ".NET 9 Minimal API", "Consulta saldo consolidado\nCache-aside · CQRS read-only")
    Container(worker, "processador-eventos", ".NET Worker Service", "Consome eventos e atualiza consolidado\nRetry · Idempotência")

    ContainerDb(pg1, "PostgreSQL\nlancamentos", "PostgreSQL 16", "Lançamentos + tabela outbox_messages")
    ContainerDb(pg2, "PostgreSQL\nconsolidado", "PostgreSQL 16", "Read model otimizado para consulta")
    ContainerDb(redis, "Redis", "Redis 7", "Cache TTL 30s para consolidado diário")
    ContainerDb(rabbit, "RabbitMQ", "RabbitMQ 3.13", "Fila de eventos de lançamentos")

    Rel(user, gw, "HTTPS")
    Rel(gw, lancamentos, "HTTP interno")
    Rel(gw, consolidado, "HTTP interno")
    Rel(lancamentos, pg1, "EF Core / SQL")
    Rel(lancamentos, rabbit, "Publica evento")
    Rel(worker, rabbit, "Consome evento")
    Rel(worker, pg2, "Atualiza consolidado")
    Rel(worker, redis, "Invalida cache")
    Rel(consolidado, redis, "Cache-aside")
    Rel(consolidado, pg2, "Fallback leitura")
```

---

## Camadas Internas — Clean Architecture

```mermaid
graph TB
    subgraph Presentation["Presentation Layer (API)"]
        P1[Controllers / Endpoints]
        P2[Middleware de Erros]
        P3[Health Checks]
    end

    subgraph Application["Application Layer"]
        A1[Commands e Handlers]
        A2[Queries e Handlers]
        A3[Pipeline Behaviors\nValidation · Logging]
        A4[DTOs e Mappers]
    end

    subgraph Domain["Domain Layer (ZERO dependências externas)"]
        D1[Entidades Ricas]
        D2[Domain Events]
        D3[Interfaces de Repositório]
        D4[Invariantes de Negócio]
        D5[Value Objects]
    end

    subgraph Infrastructure["Infrastructure Layer"]
        I1[EF Core Repositories]
        I2[RabbitMQ MessageBus]
        I3[Redis Cache]
        I4[Outbox Relay Worker]
        I5[Retry Policies]
    end

    Presentation --> Application
    Application --> Domain
    Infrastructure --> Domain

    style Domain fill:#2f9e44,color:#fff
    style Application fill:#1971c2,color:#fff
    style Presentation fill:#7048e8,color:#fff
    style Infrastructure fill:#e67700,color:#fff
```

**Regra de ouro:** as setas de dependência apontam sempre para dentro (Dependency Inversion Principle). O domínio não conhece nenhuma tecnologia externa.

### Interfaces do Domínio (contratos)

```mermaid
classDiagram
    class ILancamentoRepository {
        +AdicionarAsync(lancamento)
        +ObterPorIdAsync(id) Lancamento
        +ObterPorDataAsync(data) IEnumerable
        +AtualizarAsync(lancamento)
    }

    class IConsolidadoRepository {
        +ObterPorDataAsync(data, conta) ConsolidadoDiario
        +SalvarAsync(consolidado)
    }

    class IConsolidadoCache {
        +ObterAsync(data, conta) ConsolidadoDiario
        +SalvarAsync(consolidado)
        +InvalidateAsync(data, conta)
    }

    class IMessageBus {
        +PublishAsync(event, queue)
        +SubscribeAsync(queue, handler)
    }

    class Lancamento {
        +Id Guid
        +Tipo TipoLancamento
        +Conta string
        +Valor decimal
        +Status StatusLancamento
        +DomainEvents List
        +Criar()$ Lancamento
        +Cancelar()
        +ClearDomainEvents()
    }

    class ConsolidadoDiario {
        +Data DateTime
        +Conta string
        +TotalCreditos decimal
        +TotalDebitos decimal
        +Saldo decimal
        +AplicarLancamento(lancamento)
    }

    ILancamentoRepository ..> Lancamento
    IConsolidadoRepository ..> ConsolidadoDiario
    IConsolidadoCache ..> ConsolidadoDiario
```

---

## Fluxos de Dados Críticos

### Fluxo 1 — Registrar Lançamento (caminho feliz)

```mermaid
sequenceDiagram
    actor C as Comerciante
    participant GW as API Gateway
    participant LA as lancamentos-api
    participant DB as PostgreSQL
    participant OB as OutboxTable
    participant WK as Worker
    participant MB as RabbitMQ
    participant CA as consolidado-api
    participant RD as Redis

    C->>GW: POST /api/lancamentos
    GW->>GW: Valida JWT + Rate Limit
    GW->>LA: HTTP POST

    LA->>LA: FluentValidation
    LA->>LA: Lancamento.Criar() — invariantes de domínio
    
    LA->>DB: BEGIN TRANSACTION
    LA->>DB: INSERT lancamentos
    LA->>OB: INSERT outbox_messages
    LA->>DB: COMMIT

    LA-->>C: 201 Created {id}

    Note over WK,MB: ~2s depois (assíncrono)
    
    WK->>OB: SELECT pendentes
    WK->>MB: Publish LancamentoRegistradoEvent
    WK->>OB: UPDATE status=Enviado

    MB->>CA: Deliver evento
    CA->>CA: AplicarLancamento()
    CA->>DB: UPDATE consolidado_diario
    CA->>RD: INVALIDATE cache
```

### Fluxo 2 — Consultar Consolidado (50 rps, cache-aside)

```mermaid
sequenceDiagram
    actor C as Comerciante
    participant CA as consolidado-api
    participant RD as Redis
    participant PG as PostgreSQL

    C->>CA: GET /api/consolidado/2025-01-15

    CA->>RD: GET consolidado:2025-01-15:conta
    
    alt Cache HIT (99% das req.)
        RD-->>CA: ConsolidadoDiario
        CA-->>C: 200 OK em < 5ms
    else Cache MISS
        RD-->>CA: null
        CA->>PG: SELECT * FROM consolidado_diario
        PG-->>CA: ConsolidadoDiario
        CA->>RD: SET TTL 30s
        CA-->>C: 200 OK em ~55ms
    end
```

### Fluxo 3 — Cancelar Lançamento

```mermaid
sequenceDiagram
    actor C as Comerciante
    participant LA as lancamentos-api
    participant DB as PostgreSQL
    participant MB as RabbitMQ
    participant CA as consolidado-api
    participant RD as Redis

    C->>LA: DELETE /api/lancamentos/{id}
    LA->>DB: SELECT lancamento WHERE id=?
    
    alt Lançamento não encontrado
        LA-->>C: 404 Not Found
    else Lançamento já cancelado
        LA-->>C: 400 Bad Request
    else Lançamento ativo
        LA->>LA: lancamento.Cancelar()
        LA->>DB: UPDATE status=Cancelado
        LA->>MB: Publish LancamentoCanceladoEvent
        LA-->>C: 204 No Content

        MB->>CA: Deliver evento
        CA->>CA: Estornar do consolidado
        CA->>RD: INVALIDATE cache
    end
```

---

## Requisitos Não Funcionais

### Escalabilidade

```mermaid
graph LR
    subgraph K8S["Kubernetes (AKS)"]
        direction TB
        GW[API Gateway\n1 réplica]

        subgraph LA_POOL["lancamentos-api\nHPA: 2-5 réplicas"]
            LA1[Pod 1]
            LA2[Pod 2]
            LA3[Pod N...]
        end

        subgraph CA_POOL["consolidado-api\nHPA: 3-10 réplicas"]
            CA1[Pod 1]
            CA2[Pod 2]
            CA3[Pod N...]
        end

        subgraph WK_POOL["worker\nHPA: 1-3 réplicas"]
            WK1[Pod 1]
            WK2[Pod 2]
        end
    end

    GW --> LA_POOL
    GW --> CA_POOL
    LA_POOL --> MB[(RabbitMQ)]
    MB --> WK_POOL
```

**Estratégias:**
- **HPA (Horizontal Pod Autoscaler):** escala baseado em CPU e métricas customizadas (mensagens na fila)
- **Stateless:** todas as instâncias são intercambiáveis — sessão nunca em memória local
- **Redis externo:** cache compartilhado entre todas as réplicas do consolidado-api
- **Connection Pool:** EF Core pool de conexões configurado por instância
- **Scale-to-zero:** consolidado-api escala para zero em períodos de baixo tráfego

### Controle de Concorrência

```mermaid
sequenceDiagram
    participant W1 as Worker 1
    participant W2 as Worker 2
    participant PG as PostgreSQL
    participant RD as Redis

    Note over W1,W2: Dois workers processam o mesmo evento simultaneamente

    W1->>PG: SELECT FOR UPDATE consolidado WHERE data=D AND conta=C
    W2->>PG: SELECT FOR UPDATE consolidado WHERE data=D AND conta=C
    
    Note over PG: Pessimistic Locking — W2 aguarda
    
    W1->>PG: UPDATE TotalCreditos += valor
    W1->>PG: COMMIT
    W1->>RD: INVALIDATE cache

    Note over W2: W2 agora pode prosseguir
    W2->>PG: UPDATE TotalCreditos += valor
    W2->>PG: COMMIT
    W2->>RD: INVALIDATE cache
```

**Mecanismos implementados:**
- `ConcurrentDictionary` no repositório in-memory (thread-safe)
- `SELECT FOR UPDATE` no PostgreSQL para updates do consolidado
- Idempotência via `MessageId` único para evitar processamento duplicado
- Outbox Pattern garante exactly-once semantics na publicação

### Cache Strategy

```mermaid
flowchart TD
    REQ([Requisição GET /consolidado]) --> CACHE{Redis\ncache?}
    
    CACHE -->|HIT < 5ms| RES([Retorna 200 OK])
    CACHE -->|MISS| PG[(PostgreSQL)]
    
    PG --> STORE[Salva no Redis\nTTL = 30s]
    STORE --> RES

    EVENT([Evento recebido]) --> INV[Invalida chave\nno Redis]
    INV --> NOTE[Próxima req.\nbusca do banco]

    style CACHE fill:#f03e3e,color:#fff
    style RES fill:#2f9e44,color:#fff
    style EVENT fill:#1971c2,color:#fff
```

### Retry e Tolerância a Falhas

```mermaid
flowchart LR
    ACT[Ação] --> T1{Tentativa 1}
    T1 -->|OK| SUCCESS([Sucesso])
    T1 -->|Falha| W1[Aguarda 500ms]
    W1 --> T2{Tentativa 2}
    T2 -->|OK| SUCCESS
    T2 -->|Falha| W2[Aguarda 1000ms]
    W2 --> T3{Tentativa 3}
    T3 -->|OK| SUCCESS
    T3 -->|Falha| DLQ[(Dead Letter\nQueue)]

    style SUCCESS fill:#2f9e44,color:#fff
    style DLQ fill:#e03131,color:#fff
```

**Implementação:**
```csharp
// RetryPolicy com backoff exponencial
await RetryPolicy.ExecuteAsync(
    action,
    maxAttempts: 3,
    delay: TimeSpan.FromMilliseconds(500));
```

**Circuit Breaker (produção com Polly):**
- Abre após 5 falhas consecutivas
- Half-open após 30s para teste de recuperação
- Fallback: retorna último valor do cache ou 503 com `Retry-After`

---

## ADRs — Decisões Arquiteturais

### ADR-001 — Microsserviços vs Monolito

**Decisão:** Dois microsserviços independentes.

**Contexto:** RNF-01 exige que lançamentos funcionem mesmo se o consolidado cair. Em monolito, uma falha de memória derruba ambos simultaneamente.

| Critério | Monolito | Microsserviços |
|---|---|---|
| Isolamento de falhas | Nenhum | Total |
| Complexidade operacional | Baixa | Alta (mitigada por Docker/K8s) |
| Escalabilidade independente | Impossível | Nativa |
| RNF-01 | Violado | Atendido |

**Escolha:** Microsserviços.

---

### ADR-002 — Outbox Pattern para Atomicidade

**Problema (Dual-Write sem Outbox):**
```mermaid
sequenceDiagram
    participant API
    participant DB
    participant Broker

    API->>DB: INSERT lancamento ✅
    API->>Broker: PUBLISH evento ❌ CRASH
    
    Note over DB,Broker: Inconsistência silenciosa:\nlançamento salvo, consolidado NUNCA atualizado
```

**Solução (com Outbox):**
```mermaid
sequenceDiagram
    participant API
    participant DB
    participant Worker
    participant Broker

    API->>DB: BEGIN TRANSACTION
    API->>DB: INSERT lancamento
    API->>DB: INSERT outbox_messages
    API->>DB: COMMIT ✅

    Note over Worker: Polling a cada 2s
    Worker->>DB: SELECT outbox pendentes
    Worker->>Broker: PUBLISH (com retry)
    Worker->>DB: UPDATE status=Enviado
```

---

### ADR-003 — Cache TTL 30s

**Contexto:** 50 rps = 180.000 queries/hora sem cache — inviável em banco relacional.

| Cenário | Latência | Capacidade |
|---|---|---|
| Sem cache (banco direto) | ~50ms | ~200 rps |
| Cache HIT | < 5ms | > 10.000 rps |
| Cache MISS | ~55ms | Evento de miss raro (TTL alto) |

**Interface abstrai a implementação:**
```csharp
// PoC:      IMemoryCache (sem infra)
// Produção: IDistributedCache + Redis
// Interface: IConsolidadoCache (Domain Layer)
```

---

### ADR-004 — .NET 9 + Minimal API + CQRS

| Componente | Justificativa |
|---|---|
| **.NET 9** | ~15% menos alocações HTTP vs .NET 8; melhor startup em containers |
| **Minimal API** | Menor overhead de reflection vs MVC Controller |
| **CQRS manual** | Separação clara de Commands (escrita) e Queries (leitura) |
| **Domain Events** | Entidades publicam eventos; Application coordena, não controla |
| **Result pattern** | Erros como valores — sem exceptions para controle de fluxo |

---

## Estrutura do Projeto

```
fluxo-de-caixa-banco-carrefour/
│
├── src/
│   ├── Core/                              # Domain Layer — ZERO deps externas
│   │   ├── Entities/
│   │   │   ├── Lancamento.cs             # Entidade rica + Domain Events
│   │   │   └── ConsolidadoDiario.cs      # Projeção do saldo diário
│   │   ├── Events/
│   │   │   ├── LancamentoRegistradoEvent.cs
│   │   │   └── LancamentoCanceladoEvent.cs
│   │   └── Interfaces/
│   │       ├── ILancamentoRepository.cs
│   │       ├── IConsolidadoRepository.cs
│   │       ├── IConsolidadoCache.cs
│   │       └── IMessageBus.cs
│   │
│   ├── Application/                       # Application Layer — Orquestração
│   │   └── UseCases/
│   │       ├── Lancamentos/
│   │       │   └── Commands/
│   │       │       └── CriarLancamentoCommand.cs
│   │       │       └── CancelarLancamentoCommand.cs
│   │       └── ConsolidadoDiario/
│   │           └── Queries/
│   │               └── ObterConsolidadoDiarioQuery.cs
│   │
│   ├── Infrastructure/                    # Infrastructure Layer — Adaptadores
│   │   ├── Cache/
│   │   │   └── InMemoryConsolidadoCache.cs
│   │   ├── Messaging/
│   │   │   └── RabbitMqMessageBus.cs
│   │   ├── Persistence/
│   │   │   ├── InMemoryLancamentoRepository.cs
│   │   │   └── InMemoryConsolidadoRepository.cs
│   │   ├── Resilience/
│   │   │   └── RetryPolicy.cs
│   │   └── DependencyInjection/
│   │       └── InfrastructureServiceCollectionExtensions.cs
│   │
│   ├── Presentation/
│   │   ├── ApiLancamentos/               # API :5001
│   │   │   ├── Controllers/LancamentosController.cs
│   │   │   └── Program.cs
│   │   └── ApiConsolidado/              # API :5002
│   │       ├── Controllers/ConsolidadoController.cs
│   │       └── Program.cs
│   │
│   └── WorkerServices/
│       └── ProcessadorEventos/           # Background Worker
│           └── Worker.cs
│
├── tests/
│   └── FluxoDeCaixa.UnitTests/
│       ├── Core/Entities/
│       │   ├── LancamentoTests.cs        # 14 casos — domínio
│       │   └── ConsolidadoDiarioTests.cs # 8 casos — domínio
│       └── Application/UseCases/
│           ├── Lancamentos/Commands/
│           │   └── CriarLancamentoCommandTests.cs # 9 casos
│           └── ConsolidadoDiario/
│               └── ObterConsolidadoDiarioQueryTests.cs # 5 casos
│
├── docker-compose.yml                    # Stack completa com obs.
└── docs/
    └── solution_architecture.md
```

---

## Observabilidade

### Os Três Pilares

```mermaid
graph TB
    subgraph Traces["Distributed Tracing (Jaeger)"]
        T1[API Gateway\nSpan]
        T2[lancamentos-api\nSpan]
        T3[RabbitMQ\nSpan]
        T4[consolidado-api\nSpan]
        T1 --> T2 --> T3 --> T4
    end

    subgraph Metrics["Métricas (Prometheus + Grafana)"]
        M1[lancamentos_registrados_total]
        M2[consolidado_cache_hit_rate]
        M3[outbox_mensagens_pendentes]
        M4[http_request_duration_p99]
    end

    subgraph Logs["Logs Estruturados (Serilog)"]
        L1["{ level, traceId,\nservice, correlationId,\ndurationMs, lancamentoId }"]
    end

    OTEL[OpenTelemetry\nCollector] --> Traces
    OTEL --> Metrics
    OTEL --> Logs
```

### Alertas Críticos

| Alerta | Condição | Severidade |
|---|---|---|
| Latência alta no consolidado | P99 > 500ms por 2min | Warning |
| Taxa de erros elevada | > 5% de 5xx em 5min | Critical |
| Outbox acumulando | Pendentes > 500 por 5min | Critical |
| Redis indisponível | Connection refused | Warning |
| Serviço indisponível | Health check failing | Critical |

### Health Checks

```csharp
// Liveness: "o processo está vivo?"
app.MapHealthChecks("/health/live");

// Readiness: "pronto para receber tráfego?"
app.MapHealthChecks("/health/ready", new HealthCheckOptions {
    Predicate = c => c.Tags.Contains("ready")
});
```

---

## Segurança

```mermaid
flowchart LR
    C([Cliente]) -->|HTTPS TLS 1.3| GW[API Gateway]
    GW -->|Valida JWT| IDP[(Azure AD)]
    GW -->|mTLS interno| API[Microsserviço]
    API -->|Secrets| KV[(Azure\nKey Vault)]
    API -->|Parameterized\nQueries| PG[(PostgreSQL)]

    style GW fill:#845ef7,color:#fff
    style KV fill:#e03131,color:#fff
```

| Camada | Mecanismo |
|---|---|
| Autenticação | JWT Bearer + Azure AD |
| Autorização | Scopes: `lancamentos:write`, `consolidado:read` |
| Transporte | TLS 1.3 obrigatório, HTTPS redirect |
| Comunicação interna | mTLS via Service Mesh (Linkerd) |
| Secrets | Azure Key Vault — nunca em appsettings |
| Injeção SQL | EF Core parameterized queries |
| Rate Limiting | 100 req/min por IP (nativo .NET 9) |

---

## Arquitetura de Transição

### Strangler Fig Pattern — 3 Fases

```mermaid
timeline
    title Migração do Sistema Legado (5 meses)
    
    Meses 1-2 : Fase 1 Coexistência
              : API Gateway roteia /lancamentos para novo sistema
              : Relatórios ainda no legado
              : ETL migra histórico em batches noturnos

    Meses 3-4 : Fase 2 Shadow Mode
              : Dual-write nos dois sistemas
              : Job compara totais diariamente
              : Log de divergências para auditoria

    Mês 5+ : Fase 3 Descomissionamento
           : 100% tráfego no novo sistema
           : Legado em read-only por 90 dias
           : Desligamento após validação fiscal
```

---

## Testes e Qualidade

### Evidência de testes

<img width="1241" height="297" alt="Go NoGo Produção" src="https://github.com/paulomarne/desafio_carrefour/Evidência de testes.png" />

### Go NoGo
<img width="1258" height="690" alt="Evidência de testes" src="https://github.com/paulomarne/desafio_carrefour/Go NoGo Produção.png" />

### Pirâmide de Testes

```mermaid
graph TB
    subgraph PT["Pirâmide de Testes"]
        E2E["Testes E2E / Contrato\n(Pact.NET)\nVerificação de interfaces entre serviços"]
        INT["Testes de Integração\nWebApplicationFactory + SQLite In-Memory\nHTTP 201, 400, 404, 204"]
        APP["Testes de Application\nHandlers + Queries com Mocks\nCenários feliz + erros + edge cases"]
        DOM["Testes de Domínio\nEntidades puras sem mocks\nInvariantes + Domain Events + Cancelamento"]
    end

    DOM --> APP --> INT --> E2E
    
    style DOM fill:#2f9e44,color:#fff
    style APP fill:#1971c2,color:#fff
    style INT fill:#e67700,color:#fff
    style E2E fill:#c92a2a,color:#fff
```

### Cobertura por Suite

| Suite | Casos | Foco |
|---|---|---|
| `LancamentoTests` | 14 | Criação, cancelamento, domain events, validações, edge cases |
| `ConsolidadoDiarioTests` | 8 | Cálculo de saldo, estorno, múltiplos lançamentos |
| `CriarLancamentoCommandTests` | 6 | Handler, persistência, publicação de evento, falha de banco |
| `CancelarLancamentoCommandTests` | 3 | Cancelamento, inexistente, evento publicado |
| `ObterConsolidadoDiarioQueryTests` | 5 | Cache HIT/MISS, zeros, saldo correto |

```bash
# Rodar todos com cobertura
dotnet test --collect:"XPlat Code Coverage"
reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"coverage-report"
```

---

## Kubernetes e Infraestrutura

### Deployment Diagram

```mermaid
graph TB
    subgraph AKS["Azure Kubernetes Service (AKS)"]
        subgraph NS["namespace: fluxo-caixa"]
            ING[Ingress Controller\nNGINX]

            subgraph LA_DEP["Deployment: lancamentos-api"]
                LA1[Pod] & LA2[Pod]
            end

            subgraph CA_DEP["Deployment: consolidado-api"]
                CA1[Pod] & CA2[Pod] & CA3[Pod]
            end

            subgraph WK_DEP["Deployment: worker"]
                WK1[Pod]
            end

            SVC_LA[Service\nClusterIP :5001]
            SVC_CA[Service\nClusterIP :5002]

            HPA_LA[HPA\n2-5 réplicas\nCPU > 70%]
            HPA_CA[HPA\n3-10 réplicas\nCPU > 60%]
        end

        subgraph INFRA["Serviços Gerenciados (Azure)"]
            PG1[(Azure DB\nPostgreSQL)]
            PG2[(Azure DB\nPostgreSQL)]
            RD[(Azure Cache\nRedis)]
            SB[(Azure\nService Bus)]
        end
    end

    ING --> SVC_LA --> LA_DEP
    ING --> SVC_CA --> CA_DEP
    LA_DEP --> PG1
    LA_DEP --> SB
    WK_DEP --> SB
    WK_DEP --> PG2
    WK_DEP --> RD
    CA_DEP --> RD
    CA_DEP --> PG2
    HPA_LA -.->|escala| LA_DEP
    HPA_CA -.->|escala| CA_DEP
```

---

## Estimativa de Custos Azure

### Produção (Brazil South)

| Serviço | Configuração | USD/mês |
|---|---|---|
| AKS Node Pool | 3 nós Standard_D2s_v3 | ~$180 |
| Azure DB PostgreSQL Flexible | Burstable B2s · 2 vCores · 4GB | ~$50 |
| Azure Cache for Redis | C1 Standard · 1GB | ~$55 |
| Azure Service Bus | Standard · < 10M mensagens/mês | ~$10 |
| Azure Container Registry | Basic · 10GB | ~$5 |
| Azure Monitor + Log Analytics | 5GB/dia | ~$25 |
| **Total estimado** | | **~$325/mês** |

> **MVP / Dev:** Docker Compose local → **$0**
> **Scale-out:** Adicionar nós e zone redundancy → **~$600/mês**

---

## Evoluções Futuras

```mermaid
timeline
    title Roadmap Arquitetural

    Curto Prazo : Event Sourcing com EventStoreDB
               : Pact.NET contract testing entre serviços
               : GitHub Actions CI/CD com gate de cobertura 80%

    Médio Prazo : CQRS com read replica PostgreSQL dedicada
               : Saga Pattern para transações distribuídas
               : Multi-tenancy — múltiplas contas por comerciante

    Longo Prazo : ML para previsão de fluxo de caixa
               : Conciliação bancária via importação OFX
               : Chaos Engineering com Simmy
               : Terraform IaC para toda infra Azure
```

---

## Como Rodar Localmente

### Opção A — Docker Compose (stack completa)

```bash
git clone <url-do-repositorio>
cd fluxo-de-caixa-banco-carrefour

docker compose up --build

# APIs disponíveis:
# http://localhost:5001/swagger  — Lançamentos
# http://localhost:5002/swagger  — Consolidado
# http://localhost:15672         — RabbitMQ Management
# http://localhost:3000          — Grafana (admin/admin123)
# http://localhost:16686         — Jaeger Traces
```

### Opção B — Apenas .NET (sem Docker)

```bash
# Terminal 1 — API Lançamentos
cd src/Presentation/ApiLancamentos
dotnet run

# Terminal 2 — API Consolidado
cd src/Presentation/ApiConsolidado
dotnet run

# Terminal 3 — Worker
cd src/WorkerServices/ProcessadorEventos
dotnet run
```

### Teste Rápido (fluxo completo)

```bash
# 1. Registrar crédito
curl -X POST http://localhost:5001/api/lancamentos \
  -H "Content-Type: application/json" \
  -d '{"tipo": 2, "conta": "12345", "valor": 1500.00, "descricao": "Venda do dia"}'
# → 201 Created {"id": "...", "dataLancamento": "2025-01-15"}

# 2. Registrar débito
curl -X POST http://localhost:5001/api/lancamentos \
  -H "Content-Type: application/json" \
  -d '{"tipo": 1, "conta": "12345", "valor": 300.00, "descricao": "Aluguel"}'

# 3. Aguardar processamento assíncrono (~2s)
sleep 2

# 4. Consultar consolidado
curl http://localhost:5002/api/consolidado/2025-01-15?conta=12345
# → {"totalCreditos": 1500.00, "totalDebitos": 300.00, "saldoLiquido": 1200.00}

# 5. Cancelar lançamento
curl -X DELETE http://localhost:5001/api/lancamentos/{id}
# → 204 No Content

# 6. Rodar testes
dotnet test
```

---

## Endpoints da API

### lancamentos-api — :5001

| Método | Endpoint | Descrição | Response |
|---|---|---|---|
| `POST` | `/api/lancamentos` | Registra novo lançamento | `201 Created {id, dataLancamento}` |
| `GET` | `/api/lancamentos/{data}` | Lista lançamentos de uma data | `200 OK [{...}]` |
| `DELETE` | `/api/lancamentos/{id}` | Cancela lançamento (soft-delete) | `204 No Content` |
| `GET` | `/health` | Health check | `200 Healthy` |

### consolidado-api — :5002

| Método | Endpoint | Descrição | Response |
|---|---|---|---|
| `GET` | `/api/consolidado/{data}` | Saldo consolidado de um dia | `200 OK {totalCreditos, totalDebitos, saldoLiquido}` |
| `GET` | `/api/consolidado/hoje` | Saldo do dia atual | `200 OK {...}` |
| `GET` | `/health` | Health check | `200 Healthy` |

---

*Banco Carrefour — Desafio Arquiteto de Soluções — 2026*
