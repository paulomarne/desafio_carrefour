# Arquitetura da Solução — Sistema de Controle de Fluxo de Caixa

> **Banco Carrefour · Desafio Arquiteto de Soluções · 2026**

---

## 1. Mapeamento de Domínios Funcionais e Capacidades de Negócio

### 1.1 Bounded Contexts (Domain-Driven Design)

```mermaid
graph TB
    subgraph BC_LANCAMENTOS["Bounded Context: Lançamentos (Core Domain)"]
        direction LR
        ENT_L[Lancamento\n― Id: Guid\n― Tipo: Débito/Crédito\n― Conta: string\n― Valor: decimal\n― Status: Ativo/Cancelado\n― DomainEvents]
        EV1[LancamentoRegistrado\nDomainEvent]
        EV2[LancamentoCancelado\nDomainEvent]
        ENT_L -->|publica| EV1
        ENT_L -->|publica| EV2
    end

    subgraph BC_CONSOLIDADO["Bounded Context: Consolidação Diária (Core Domain)"]
        direction LR
        ENT_C[ConsolidadoDiario\n― Data: DateTime\n― Conta: string\n― TotalDebitos: decimal\n― TotalCreditos: decimal\n― Saldo: computed]
        OP1[AplicarLancamento]
        ENT_C --> OP1
    end

    subgraph SK["Shared Kernel"]
        IDomainEvent[IDomainEvent\ninterface]
        IRepository[IRepository\ninterface genérica]
    end

    EV1 -->|integração via broker| BC_CONSOLIDADO
    EV2 -->|estorno via broker| BC_CONSOLIDADO

    style BC_LANCAMENTOS fill:#1864ab,color:#fff
    style BC_CONSOLIDADO fill:#2b8a3e,color:#fff
    style SK fill:#5f3dc4,color:#fff
```

### 1.2 Linguagem Ubíqua

| Termo | Contexto | Definição |
|---|---|---|
| **Lançamento** | Lançamentos | Registro atômico de entrada ou saída financeira na conta de um comerciante |
| **Débito** | Lançamentos | Saída de caixa — reduz TotalDebitos do consolidado |
| **Crédito** | Lançamentos | Entrada de caixa — aumenta TotalCreditos do consolidado |
| **Cancelamento** | Lançamentos | Reversão de um lançamento ativo via soft-delete |
| **Consolidado Diário** | Consolidação | Projeção calculada e persistida do saldo financeiro de um dia |
| **Saldo Líquido** | Consolidação | `TotalCréditos − TotalDébitos` calculado no domínio |
| **Estorno** | Consolidação | Efeito no consolidado quando um lançamento é cancelado |

### 1.3 Capacidades de Negócio

| ID | Capacidade | Domínio | Criticidade | SLA |
|---|---|---|---|---|
| CAP-01 | Registrar lançamento de débito/crédito | Lançamentos | Crítica | 99.9% |
| CAP-02 | Cancelar lançamento (soft-delete) | Lançamentos | Alta | 99.5% |
| CAP-03 | Consultar lançamentos por data | Lançamentos | Alta | 99.5% |
| CAP-04 | Consultar saldo consolidado do dia | Consolidação | Crítica | 99.9% |
| CAP-05 | Reprocessar consolidado (admin) | Consolidação | Média | 99.0% |

---

## 2. Requisitos Funcionais e Não Funcionais Refinados

### 2.1 Requisitos Funcionais

#### Serviço de Lançamentos

| ID | Requisito | Critério de Aceitação |
|---|---|---|
| RF-01 | Registrar lançamento de débito | Persistido com tipo=DEBITO, valor>0, conta, descrição, data ≤ hoje |
| RF-02 | Registrar lançamento de crédito | Persistido com tipo=CREDITO, valor>0, conta, descrição, data ≤ hoje |
| RF-03 | Listar lançamentos por data | Retorna todos (ativos e cancelados) de uma data no formato `yyyy-MM-dd` |
| RF-04 | Cancelar lançamento | Soft-delete — status=Cancelado; evento publicado para estornar no consolidado |
| RF-05 | Validar lançamento | Rejeitar: valor ≤ 0, valor > 10M, descrição vazia, descrição > 255 chars, data futura |

#### Serviço de Consolidado Diário

| ID | Requisito | Critério de Aceitação |
|---|---|---|
| RF-06 | Consultar saldo por data | Retorna TotalCréditos, TotalDébitos, SaldoLíquido da data informada |
| RF-07 | Consultar saldo do dia atual | Atalho para data = hoje |
| RF-08 | Consolidado zerado para dia sem lançamentos | Retorna zeros — nunca 404 |

### 2.2 Requisitos Não Funcionais

| ID | Categoria | Requisito | Meta | Mecanismo |
|---|---|---|---|---|
| **RNF-01** | **Disponibilidade** | Lançamentos independentes do consolidado | 100% isolamento | Microsserviços + mensageria assíncrona |
| **RNF-02** | **Throughput** | 50 rps no consolidado | P99 < 200ms | Cache Redis TTL 30s |
| **RNF-03** | **Confiabilidade** | Máx. 5% perda no consolidado | ≥ 95% sucesso | Cache como fallback; sem SPOF |
| RNF-04 | Atomicidade | Lançamento salvo = evento publicado | Zero inconsistência | Outbox Pattern |
| RNF-05 | Consistência | Atualização eventual do consolidado | Defasagem < 5s | OutboxRelayWorker poll 2s |
| RNF-06 | Rastreabilidade | Logs com CorrelationId | 100% requests | Serilog + OpenTelemetry |
| RNF-07 | Escalabilidade | Horizontal sem estado local | Zero in-memory sessions | Stateless + Redis externo |
| RNF-08 | Resiliência | Falha no broker não derruba lançamentos | Zero perda | Outbox persiste antes de publicar |
| RNF-09 | Concorrência | Updates simultâneos no consolidado | Sem race condition | `SELECT FOR UPDATE` + ConcurrentDictionary |
| RNF-10 | Segurança | Endpoints protegidos | 100% autenticado | JWT + mTLS + Key Vault |

---

## 3. Desenho da Solução Completo

### 3.1 Visão Geral (C4 — Nível de Contexto)

```mermaid
C4Context
    title Sistema Fluxo de Caixa — Visão de Contexto

    Person(comerciante, "Comerciante", "Registra débitos/créditos e consulta saldo diário")
    Person(admin, "Administrador", "Monitora saúde e reprocessa dados")

    System_Boundary(sistema, "Sistema Fluxo de Caixa") {
        System(lancamentos, "Serviço de Lançamentos", "Registra e gerencia lançamentos financeiros")
        System(consolidado, "Serviço de Consolidado", "Fornece saldo consolidado em tempo real")
    }

    System_Ext(idp, "Identity Provider\n(Azure AD B2C)", "Emite e valida tokens JWT")
    System_Ext(observ, "Observabilidade\n(Grafana · Jaeger · Prometheus)", "Monitora saúde e rastreia requisições")

    Rel(comerciante, lancamentos, "Registra lançamentos", "HTTPS/REST")
    Rel(comerciante, consolidado, "Consulta saldo", "HTTPS/REST")
    Rel(admin, observ, "Monitora", "HTTPS")
    Rel(lancamentos, idp, "Valida JWT", "HTTPS")
    Rel(consolidado, idp, "Valida JWT", "HTTPS")
    Rel(lancamentos, observ, "Envia traces/métricas", "OTLP")
    Rel(consolidado, observ, "Envia traces/métricas", "OTLP")
```

### 3.2 Visão de Contêineres (C4 — Nível 2)

```mermaid
C4Container
    title Sistema Fluxo de Caixa — Contêineres

    Person(user, "Comerciante")

    Container(gw, "API Gateway", "NGINX / Azure APIM", "Autenticação JWT, Rate Limiting (100 req/min), TLS 1.3, CORS")

    System_Boundary(fluxo, "Fluxo de Caixa") {
        Container(lancApi, "lancamentos-api", ".NET 9 Minimal API · :5001", "CQRS Commands · DDD · Outbox Pattern · Retry")
        Container(consApi, "consolidado-api", ".NET 9 Minimal API · :5002", "CQRS Queries · Cache-Aside · Read Model")
        Container(worker, "processador-eventos", ".NET Worker Service", "Consome eventos · Atualiza consolidado · Idempotente")

        ContainerDb(pgLanc, "PostgreSQL\nlancamentos", "PostgreSQL 16", "Tabelas: lancamentos, outbox_messages")
        ContainerDb(pgCons, "PostgreSQL\nconsolidado", "PostgreSQL 16", "Tabela: consolidado_diario (read model)")
        ContainerDb(redis, "Redis", "Redis 7 (TTL 30s)", "Cache para consolidado_diario por data+conta")
        ContainerDb(rabbit, "RabbitMQ", "RabbitMQ 3.13", "Filas: lancamentos, lancamentos-cancelados")
    }

    Rel(user, gw, "HTTPS")
    Rel(gw, lancApi, "HTTP/mTLS")
    Rel(gw, consApi, "HTTP/mTLS")
    Rel(lancApi, pgLanc, "EF Core · SQL")
    Rel(lancApi, rabbit, "Publica via Outbox")
    Rel(worker, rabbit, "Consome / Ack")
    Rel(worker, pgCons, "Upsert consolidado")
    Rel(worker, redis, "Invalida cache")
    Rel(consApi, redis, "Cache-Aside GET")
    Rel(consApi, pgCons, "Fallback SQL")
```

### 3.3 Camadas Internas — Clean Architecture

```mermaid
graph TB
    subgraph PRES["Presentation Layer"]
        style PRES fill:#7048e8,color:#fff
        PC[Controllers\nProgram.cs\nMiddlewares\nHealth Checks]
    end

    subgraph APP["Application Layer"]
        style APP fill:#1971c2,color:#fff
        AC[Commands / Queries\nHandlers\nPipeline Behaviors\nDTOs / Mappers]
    end

    subgraph DOM["Domain Layer (Core) — ZERO dependências externas"]
        style DOM fill:#2f9e44,color:#fff
        DC[Entidades Ricas\nDomain Events\nInterfaces de Repositório\nInvariantes de Negócio\nValue Objects]
    end

    subgraph INFRA["Infrastructure Layer"]
        style INFRA fill:#e67700,color:#fff
        IC[EF Core Repositories\nRabbitMQ MessageBus\nRedis Cache\nOutbox Relay\nRetry Policies\nDI Extensions]
    end

    PRES -->|depende de| APP
    APP -->|depende de| DOM
    INFRA -->|implementa interfaces de| DOM
    INFRA -.->|não depende de| APP

    note1["Dependency Inversion:\nDomínio define contratos,\nInfra implementa"]
```

---

## 4. Padrões Arquiteturais em Detalhes

### 4.1 CQRS — Separação de Leitura e Escrita

```mermaid
graph LR
    subgraph WRITE["Write Side (Commands)"]
        CMD[CriarLancamentoCommand\nCancelarLancamentoCommand]
        CMD --> REPO[(PostgreSQL\nlancamentos)]
        CMD --> OB[(Outbox Table)]
    end

    subgraph READ["Read Side (Queries)"]
        QRY[ObterConsolidadoDiarioQuery]
        QRY --> CACHE[(Redis Cache)]
        QRY --> REPO2[(PostgreSQL\nconsolidado\nread model)]
    end

    subgraph SYNC["Sincronização Assíncrona"]
        OB --> WK[Worker Service]
        WK --> MB[(RabbitMQ)]
        MB --> REPO2
        MB --> CACHE
    end

    style WRITE fill:#1864ab,color:#fff
    style READ fill:#2b8a3e,color:#fff
    style SYNC fill:#c92a2a,color:#fff
```

### 4.2 Outbox Pattern — Atomicidade Garantida

```mermaid
sequenceDiagram
    participant APP as Application
    participant DB as PostgreSQL
    participant WK as OutboxRelayWorker
    participant MB as RabbitMQ

    APP->>DB: BEGIN TRANSACTION
    APP->>DB: INSERT INTO lancamentos (id, tipo, conta, valor, ...)
    APP->>DB: INSERT INTO outbox_messages (id, payload, status='Pendente')
    APP->>DB: COMMIT ✅

    APP-->>APP: Retorna 201 Created imediatamente

    loop A cada 2 segundos
        WK->>DB: SELECT * FROM outbox_messages WHERE status='Pendente'
        WK->>MB: PUBLISH evento (com retry 3x)
        WK->>DB: UPDATE outbox_messages SET status='Enviado'
    end
```

### 4.3 Domain Events — Entidade Rica

```mermaid
sequenceDiagram
    participant CTRL as Controller
    participant CMD as Command Handler
    participant ENT as Lancamento (Entity)
    participant BUS as MessageBus

    CTRL->>CMD: ExecutarAsync(request)
    CMD->>ENT: Lancamento.Criar(tipo, conta, valor, desc)
    ENT->>ENT: Valida invariantes de domínio
    ENT->>ENT: Adiciona LancamentoRegistradoDomainEvent
    ENT-->>CMD: retorna entidade com evento

    CMD->>CMD: repository.AdicionarAsync(lancamento)
    
    loop Para cada Domain Event
        CMD->>BUS: PublishAsync(evento, fila)
    end
    
    CMD->>ENT: ClearDomainEvents()
    CMD-->>CTRL: CriarLancamentoResult {Id, Data}
```

---

## 5. Requisitos Não Funcionais em Detalhe

### 5.1 Estratégia de Escalabilidade

```mermaid
flowchart TB
    subgraph TIER1["Camada de Entrada"]
        GW[API Gateway\nLoad Balancer]
    end

    subgraph TIER2["Camada de Aplicação (Stateless)"]
        LA[lancamentos-api\nHPA: 2-5 pods\nCPU target: 70%]
        CA[consolidado-api\nHPA: 3-10 pods\nCPU target: 60%]
    end

    subgraph TIER3["Camada de Processamento Assíncrono"]
        WK[Worker Service\nHPA: 1-3 pods\nMétrica: fila > 100 msgs]
    end

    subgraph TIER4["Camada de Dados (Gerenciados)"]
        PG1[(PostgreSQL\nReadReplica opcional)]
        RD[(Redis Cluster\nHA Mode)]
        MB[(RabbitMQ\nHA Queue Mirroring)]
    end

    GW --> LA & CA
    LA --> MB
    CA --> RD
    WK --> MB
    WK --> RD

    style TIER2 fill:#1864ab,color:#fff
    style TIER3 fill:#e67700,color:#fff
```

**Princípios:**
- **Zero in-memory state:** toda sessão em Redis externo; qualquer pod pode responder qualquer request
- **HPA metric-based:** consolidado-api escala por CPU; worker escala pelo tamanho da fila RabbitMQ
- **Connection pooling:** EF Core pool de conexões configurado por número de CPUs da instância
- **Graceful shutdown:** draining de conexões em `IHostApplicationLifetime.ApplicationStopping`

### 5.2 Controle de Concorrência

| Camada | Mecanismo | Cenário |
|---|---|---|
| In-Memory (PoC) | `ConcurrentDictionary` | Thread-safety nas operações de repositório |
| PostgreSQL | `SELECT FOR UPDATE` | Dois workers atualizando o mesmo consolidado |
| RabbitMQ | `BasicQos(prefetchCount: 1)` | Worker processa uma mensagem por vez |
| Idempotência | `MessageId` único + `IF NOT EXISTS` | Previne processamento duplicado em retry |

### 5.3 Cache Strategy (Cache-Aside)

```mermaid
flowchart TD
    A([Requisição]) --> B{Redis\nCache?}
    B -- HIT\n< 5ms --> C([Retorna ao cliente])
    B -- MISS\n~55ms --> D[(PostgreSQL)]
    D --> E[Salva Redis\nTTL = 30s]
    E --> C

    F([Evento de lançamento]) --> G[Worker invalida\nchave no Redis]
    G --> H[Próxima request\nbusca banco e popula cache]

    style B fill:#e03131,color:#fff
    style C fill:#2f9e44,color:#fff
    style F fill:#1971c2,color:#fff
```

### 5.4 Tolerância a Falhas

```mermaid
stateDiagram-v2
    [*] --> Fechado : Circuito normal
    Fechado --> Aberto : 5 falhas consecutivas
    Aberto --> MeioAberto : Após 30 segundos
    MeioAberto --> Fechado : Requisição teste OK
    MeioAberto --> Aberto : Requisição teste falha

    state Fechado {
        [*] --> Operando
    }
    state Aberto {
        [*] --> Fallback : Retorna cache / 503
    }
    state MeioAberto {
        [*] --> TestandoRecuperacao
    }
```

**Políticas implementadas:**
- `RetryPolicy`: 3 tentativas com backoff 500ms → 1s → 2s
- Circuit Breaker (Polly em produção): protege PostgreSQL e RabbitMQ
- Health Checks Liveness/Readiness no Kubernetes: pods não recebem tráfego até estarem prontos
- Dead Letter Queue: mensagens que falharam 3x vão para DLQ para análise manual

### 5.5 Observabilidade Completa

```mermaid
graph TB
    subgraph APPS["Microsserviços"]
        LA[lancamentos-api]
        CA[consolidado-api]
        WK[worker]
    end

    subgraph OTEL["OpenTelemetry SDK"]
        T[Traces]
        M[Metrics]
        L[Logs]
    end

    subgraph BACKEND["Backends de Observabilidade"]
        JA[Jaeger\nDistributed Tracing]
        PR[Prometheus\nMetrics Store]
        GR[Grafana\nDashboards + Alertas]
        LK[Loki / Log Analytics\nLog Aggregation]
    end

    APPS --> OTEL
    T --> JA
    M --> PR
    L --> LK
    PR --> GR
    JA --> GR

    style OTEL fill:#5f3dc4,color:#fff
    style BACKEND fill:#1864ab,color:#fff
```

**Métricas customizadas de negócio:**
```csharp
var lancamentosTotal = meter.CreateCounter<long>("lancamentos_registrados_total",
    description: "Total de lançamentos registrados");

var cacheHitRate = meter.CreateObservableGauge<double>("consolidado_cache_hit_rate",
    description: "Taxa de cache hit no consolidado");

var outboxPendentes = meter.CreateObservableGauge<int>("outbox_mensagens_pendentes",
    description: "Mensagens aguardando envio no Outbox");
```

---

## 6. Estrutura do Projeto — Clean Architecture

```
fluxo-de-caixa-banco-carrefour/
│
├── src/
│   ├── Core/                              ← Domain Layer
│   │   ├── Entities/
│   │   │   ├── Lancamento.cs             ← Entidade rica com Domain Events
│   │   │   └── ConsolidadoDiario.cs      ← Agregado de consolidação
│   │   ├── Events/
│   │   │   ├── LancamentoRegistradoEvent.cs
│   │   │   └── LancamentoCanceladoEvent.cs
│   │   └── Interfaces/
│   │       ├── ILancamentoRepository.cs
│   │       ├── IConsolidadoRepository.cs
│   │       ├── IConsolidadoCache.cs
│   │       └── IMessageBus.cs
│   │
│   ├── Application/                       ← Application Layer
│   │   └── UseCases/
│   │       ├── Lancamentos/Commands/
│   │       │   ├── CriarLancamentoCommand.cs
│   │       │   └── CancelarLancamentoCommand.cs
│   │       └── ConsolidadoDiario/Queries/
│   │           └── ObterConsolidadoDiarioQuery.cs
│   │
│   ├── Infrastructure/                    ← Infrastructure Layer
│   │   ├── Cache/InMemoryConsolidadoCache.cs
│   │   ├── Messaging/RabbitMqMessageBus.cs
│   │   ├── Persistence/
│   │   │   ├── InMemoryLancamentoRepository.cs
│   │   │   └── InMemoryConsolidadoRepository.cs
│   │   ├── Resilience/RetryPolicy.cs
│   │   └── DependencyInjection/
│   │       └── InfrastructureServiceCollectionExtensions.cs
│   │
│   ├── Presentation/
│   │   ├── ApiLancamentos/               ← :5001
│   │   │   ├── Controllers/LancamentosController.cs
│   │   │   └── Program.cs
│   │   └── ApiConsolidado/              ← :5002
│   │       ├── Controllers/ConsolidadoController.cs
│   │       └── Program.cs
│   │
│   └── WorkerServices/ProcessadorEventos/
│       └── Worker.cs
│
├── tests/
│   └── FluxoDeCaixa.UnitTests/
│       ├── Core/Entities/
│       │   ├── LancamentoTests.cs        ← 14 casos
│       │   └── ConsolidadoDiarioTests.cs ← 8 casos
│       └── Application/UseCases/
│           ├── Lancamentos/
│           │   └── CriarLancamentoCommandTests.cs ← 9 casos
│           └── ConsolidadoDiario/
│               └── ObterConsolidadoDiarioQueryTests.cs ← 5 casos
│
└── docker-compose.yml                    ← Stack completa: APIs + PG + Redis + RabbitMQ + Obs.
```

---

## 7. Testes e Qualidade

### 7.1 Estratégia de Testes

```mermaid
pyramid
    "Testes de Contrato (Pact.NET)" : 0
    "Testes E2E (WebApplicationFactory)" : 0
    "Testes de Integração (SQLite InMemory)" : 0
    "Testes de Application (Mocks)" : 0
    "Testes de Domínio (Puras)" : 0
```

| Camada | Framework | Foco | Casos |
|---|---|---|---|
| Domínio | xUnit + FluentAssertions | Invariantes, Domain Events, cancelamento | 22 |
| Application | xUnit + Moq | Commands, Queries, cenários de erro | 14 |
| Integração | WebApplicationFactory | HTTP completo, banco real em memória | Próximo passo |
| Contrato | Pact.NET | Compatibilidade entre lancamentos-api e consolidado-api | Próximo passo |

### 7.2 Cenários de Erro Cobertos

```mermaid
flowchart LR
    subgraph ERRO["Cenários de Erro Testados"]
        E1[Conta vazia]
        E2[Valor zero ou negativo]
        E3[Valor acima de 10M]
        E4[Descrição vazia]
        E5[Descrição > 255 chars]
        E6[Data futura]
        E7[Cancelar lançamento inexistente]
        E8[Cancelar lançamento já cancelado]
        E9[Cache MISS com banco respondendo]
        E10[Falha de persistência não publica evento]
    end
```

### 7.3 Próximos Passos de Qualidade

```csharp
// Teste de Contrato (Pact.NET) — a implementar
[Fact]
public async Task Consolidado_Api_Deve_Respeitar_Contrato_Lancamentos()
{
    // Verifica que consolidado-api processa corretamente
    // o payload publicado por lancamentos-api
    // Previne breaking changes silenciosos entre serviços
}
```

---

## 8. Arquitetura de Transição (AS-IS → TO-BE)

```mermaid
journey
    title Jornada de Migração do Legado
    section Mês 1-2: Coexistência
        Deploy novo sistema: 5: Time
        Gateway roteia /lancamentos para novo: 4: Time
        ETL migra histórico em batches: 3: Time
        Validação dos primeiros dados: 4: Time
    section Mês 3-4: Shadow Mode
        Dual-write nos dois sistemas: 3: Time
        Job compara totais diariamente: 4: Time
        Zero divergências por 2 semanas: 5: Time
    section Mês 5+: Descomissionamento
        100% tráfego no novo sistema: 5: Time
        Legado em read-only 90 dias: 4: Time
        Desligamento validado: 5: Time
```

---

## 9. Decisões Arquiteturais (ADRs)

### ADR-001: Microsserviços

**Status:** Aceito

**Contexto:** RNF-01 exige isolamento total. Monolito viola este requisito por compartilhar processo.

**Consequências:**
- Isolamento de falhas garantido
- Escalonamento independente
- Complexidade operacional mitigada por Docker/Kubernetes

### ADR-002: Outbox Pattern

**Status:** Aceito

**Contexto:** Dual-write sem Outbox cria janela de inconsistência silenciosa entre lançamento salvo e evento publicado.

**Consequências:**
- Atomicidade garantida entre persistência e mensagem
- Latência adicional de ~2s (aceitável para consistência eventual)
- Auditoria nativa na tabela `outbox_messages`

### ADR-003: Cache TTL 30s com Redis

**Status:** Aceito

**Contexto:** 50 rps = 180.000 queries/hora sem cache. Banco PostgreSQL satura com ~200 rps.

**Consequências:**
- Cache HIT: <5ms, >10.000 rps
- Consistência eventual com defasagem máxima de 30s (aceitável)
- Interface `IConsolidadoCache` permite trocar implementação sem tocar no domínio

### ADR-004: .NET 9 Minimal API + CQRS Manual

**Status:** Aceito

**Contexto:** Performance e startup em containers são críticos.

**Consequências:**
- ~15% menos alocações HTTP vs .NET 8
- CQRS sem MediatR reduz overhead de reflection
- Testabilidade total dos handlers isolados

### ADR-005: PostgreSQL (Produção) / SQLite ou InMemory (PoC)

**Status:** Aceito

**Contexto:** PoC deve rodar sem infraestrutura externa para avaliação rápida.

**Consequências:**
- Troca é transparente via interfaces do repositório
- Mesma lógica de domínio em todos os ambientes
- Migrations EF Core funcionam nos dois providers

---

## 10. Evoluções Futuras

```mermaid
graph LR
    subgraph NOW["Implementado"]
        A[Clean Architecture]
        B[CQRS + DDD]
        C[Domain Events]
        D[Outbox Pattern]
        E[Cache Redis]
        F[Retry Policy]
        G[RabbitMQ]
        H[Docker Compose]
    end

    subgraph NEXT["Próximo Ciclo"]
        I[Event Sourcing\nEventStoreDB]
        J[Pact.NET\nContract Testing]
        K[GitHub Actions\nCI/CD + Coverage Gate]
        L[Kubernetes\nHelm Charts]
    end

    subgraph FUTURE["Futuro"]
        M[Saga Pattern\nTransações Distribuídas]
        N[Multi-Tenancy\nMúltiplas contas]
        O[ML Previsão\nFluxo de Caixa]
        P[Chaos Engineering\nSimmy / Chaos Monkey]
    end

    NOW --> NEXT --> FUTURE
```

---

*Banco Carrefour · Desafio Arquiteto de Soluções · Paulo Marne · 2026*
