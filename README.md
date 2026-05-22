# 💰 Arquitetura de Soluções — Controle de Fluxo de Caixa

> **Desafio:** Arquiteto de Soluções — Banco Carrefour · Janeiro 2025  
> **Stack:** .NET 9 · PostgreSQL · Redis · Azure Service Bus · Docker · CQRS · DDD · Outbox Pattern

---

## ⚡ TL;DR — O que esta solução resolve

O desafio tem dois requisitos não-funcionais que ditam **toda** a arquitetura:

```
RNF-01: Lançamentos NÃO podem cair se o consolidado cair
RNF-02: Consolidado suporta 50 rps com máx. 5% de perda
```

| Requisito | Solução Escolhida | Por quê |
|---|---|---|
| **RNF-01** | Microsserviços + Mensageria Assíncrona | Monolito compartilha processo — uma falha derruba os dois |
| **RNF-02** | Cache Redis TTL 30s + consistência eventual | 50 rps = 180k queries/hora sem cache; inviável em banco relacional |
| **Atomicidade** | Outbox Pattern | Garante que lançamento salvo = evento publicado, na mesma transação |

---

## 📋 Checklist Completo do Desafio

### Requisitos Obrigatórios

| Item | Status | Onde está |
|---|---|---|
| Serviço de controle de lançamentos | ✅ | `src/FluxoCaixa.Lancamentos.*` |
| Serviço do consolidado diário | ✅ | `src/FluxoCaixa.Consolidado.*` |
| Mapeamento de domínios e capacidades | ✅ | [Seção: Domínios Funcionais](#domínios-funcionais-ddd) |
| Refinamento de requisitos funcionais e NF | ✅ | [Seção: Requisitos](#requisitos) |
| Desenho da solução completo (Arquitetura Alvo) | ✅ | [Seção: Arquitetura Alvo](#arquitetura-alvo) |
| Justificativa de ferramentas e tipo de arquitetura | ✅ | [ADRs](#adrs--architecture-decision-records) |
| Testes | ✅ | `tests/` — 34 casos (unitários + integração) |
| README com instruções para rodar | ✅ | [Como Rodar](#como-rodar-localmente) |
| Repositório público com toda documentação | ✅ | Este repositório |

### Requisitos Diferenciais

| Item | Status | Onde está |
|---|---|---|
| Arquitetura de Transição (migração de legado) | ✅ | [Seção: Transição](#arquitetura-de-transição) |
| Estimativa de custos de infraestrutura | ✅ | [Seção: Custos](#estimativa-de-custos-azure) |
| Monitoramento e Observabilidade | ✅ | [Seção: Observabilidade](#monitoramento--observabilidade) |
| Critérios de segurança para consumo de serviços | ✅ | [Seção: Segurança](#segurança) |

---

## Índice

1. [Domínios Funcionais (DDD)](#domínios-funcionais-ddd)
2. [Requisitos](#requisitos)
3. [Decisões Arquiteturais (ADRs)](#adrs--architecture-decision-records)
4. [Arquitetura Alvo](#arquitetura-alvo)
5. [Fluxos de Dados Críticos](#fluxos-de-dados-críticos)
6. [Estrutura do Projeto](#estrutura-do-projeto)
7. [Como Rodar Localmente](#como-rodar-localmente)
8. [Endpoints da API](#endpoints-da-api)
9. [Testes](#testes)
10. [Segurança](#segurança)
11. [Monitoramento & Observabilidade](#monitoramento--observabilidade)
12. [Arquitetura de Transição](#arquitetura-de-transição)
13. [Estimativa de Custos Azure](#estimativa-de-custos-azure)
14. [PoC vs Produção](#poc-vs-produção)
15. [Evoluções Futuras](#evoluções-futuras)

---

## Domínios Funcionais (DDD)

### Bounded Contexts

```
┌───────────────────────────────────────────────────────────────────────────┐
│                        FLUXO DE CAIXA                                     │
│                                                                           │
│  ┌─────────────────────────────┐    ┌──────────────────────────────────┐  │
│  │   LANÇAMENTOS (Core Domain) │    │  CONSOLIDAÇÃO DIÁRIA (Core)      │  │
│  │                             │    │                                  │  │
│  │  Entidades:                 │    │  Entidades:                      │  │
│  │  · Lancamento               │    │  · ConsolidadoDiario             │  │
│  │  · TipoLancamento           │    │                                  │  │
│  │                             │    │  Projeção calculada:             │  │
│  │  Comandos:                  │    │  · TotalCréditos                 │  │
│  │  · RegistrarLancamento      │    │  · TotalDébitos                  │  │
│  │  · CancelarLancamento       │    │  · SaldoLíquido = Créd − Déb    │  │
│  │                             │    │                                  │  │
│  │  Eventos publicados:        │───▶│  Atualizada via eventos          │  │
│  │  · LancamentoRegistrado     │    │  (consistência eventual)         │  │
│  │  · LancamentoCancelado      │    │                                  │  │
│  │                             │    │  Queries:                        │  │
│  │  Invariantes de domínio:    │    │  · ObterConsolidadoDiario        │  │
│  │  · Valor > 0                │    │  · ObterPorPeriodo (máx 365d)    │  │
│  │  · Descrição obrigatória    │    │                                  │  │
│  │  · Sem datas futuras        │    │  Cache TTL 30s:                  │  │
│  │  · Valor ≤ 10.000.000       │    │  Memory (PoC) → Redis (Prod)     │  │
│  └─────────────────────────────┘    └──────────────────────────────────┘  │
└───────────────────────────────────────────────────────────────────────────┘
```

### Linguagem Ubíqua

| Termo | Definição no Domínio |
|---|---|
| **Lançamento** | Registro financeiro de entrada ou saída na conta do comerciante |
| **Débito** (tipo 1) | Saída de caixa — reduz o saldo disponível |
| **Crédito** (tipo 2) | Entrada de caixa — aumenta o saldo disponível |
| **Consolidado Diário** | Projeção calculada do saldo total de um dia |
| **Saldo Líquido** | TotalCréditos − TotalDébitos de um dia |
| **Estorno** | Reversão de um lançamento cancelado no consolidado |

### Capacidades de Negócio

| Capacidade | Domínio | Criticidade |
|---|---|---|
| Registrar lançamento (débito/crédito) | Lançamentos | 🔴 Crítica |
| Consultar lançamentos por data | Lançamentos | 🟠 Alta |
| Cancelar lançamento | Lançamentos | 🟠 Alta |
| Consultar saldo consolidado do dia | Consolidação | 🔴 Crítica |
| Consultar saldo por período | Consolidação | 🟠 Alta |
| Reprocessar consolidado (admin) | Consolidação | 🟡 Média |

---

## Requisitos

### Requisitos Funcionais

#### Serviço de Lançamentos

| ID | Requisito | Critério de Aceitação |
|---|---|---|
| RF-01 | Registrar lançamento de débito | Persistido com tipo=DEBITO, valor>0, data, descrição |
| RF-02 | Registrar lançamento de crédito | Persistido com tipo=CREDITO, valor>0, data, descrição |
| RF-03 | Listar lançamentos por data | Retorna todos os lançamentos de uma data (incluindo cancelados) |
| RF-04 | Cancelar lançamento | Soft-delete; evento publicado para atualizar consolidado |
| RF-05 | Validar lançamento | Rejeitar valor ≤ 0, descrição vazia, data futura, tipo inválido |

#### Serviço de Consolidado Diário

| ID | Requisito | Critério de Aceitação |
|---|---|---|
| RF-06 | Consultar saldo consolidado por data | Retorna TotalCréditos, TotalDébitos, SaldoLíquido |
| RF-07 | Consultar consolidado por período | Retorna lista diária; período máximo 365 dias |
| RF-08 | Consolidado zerado para dia sem lançamentos | Retorna zeros em vez de 404 |

### Requisitos Não Funcionais

| ID | Categoria | Requisito | Meta | Como Atendido |
|---|---|---|---|---|
| **RNF-01** | **Disponibilidade** | Lançamentos independentes do consolidado | 100% isolamento | Microsserviços + mensageria assíncrona |
| **RNF-02** | **Throughput** | 50 rps no consolidado | P99 < 200ms | Cache TTL 30s + projeção pré-computada |
| **RNF-03** | **Confiabilidade** | Máx. 5% perda no consolidado | ≥ 95% sucesso | Cache como fallback; sem SPOF |
| RNF-04 | Consistência | Atualização eventual do consolidado | Defasagem < 5s | Outbox Relay poll a cada 2s |
| RNF-05 | Rastreabilidade | Logs com correlationId | 100% requests | Serilog + OpenTelemetry |
| RNF-06 | Escalabilidade | Horizontal sem estado em memória | Zero in-memory sessions | Stateless + Redis externo |
| RNF-07 | Resiliência | Falha no broker não derruba lançamentos | Zero perda de dados | Outbox persiste antes de publicar |

---

## ADRs — Architecture Decision Records

### ADR-001 — Microsserviços vs Monolito

**Decisão:** Dois microsserviços independentes (`lancamentos-api` e `consolidado-api`).

**Contexto:** O RNF-01 exige que os lançamentos continuem funcionando mesmo se o consolidado cair. Em um monolito, ambos compartilham o mesmo processo — uma falha de memória ou CPU derruba os dois simultaneamente.

**Consequências:**
- ✅ Isolamento total de falhas
- ✅ Escalonamento independente (consolidado: 3 réplicas; lançamentos: 2)
- ❌ Complexidade operacional; mitigada por Docker Compose (local) e AKS (produção)

**Alternativa descartada:** Monolito Modular — atenderia escalonamento mas viola RNF-01.

---

### ADR-002 — Outbox Pattern para Atomicidade

**Decisão:** Transactional Outbox + BackgroundService (OutboxRelayWorker).

**Problema resolvido — Dual Write:**
```
❌ SEM OUTBOX (perigoso):
  1. INSERT lancamentos → OK
  2. Publica no broker → FALHA (crash, timeout)
  Resultado: lançamento salvo, consolidado NUNCA atualizado. Inconsistência silenciosa.

✅ COM OUTBOX (seguro):
  Transação única:
    INSERT lancamentos
    INSERT outbox_messages   ← mesmo COMMIT
  OutboxRelayWorker publica de forma assíncrona.
  Máximo 5 tentativas com log de erro.
```

**Consequências:**
- ✅ Atomicidade entre persistência e publicação de evento
- ✅ Auditoria nativa (tabela `outbox_messages` registra tudo)
- ❌ Latência de ~2s entre lançamento e atualização do consolidado (aceitável)

---

### ADR-003 — Cache TTL 30s para Consolidado

**Decisão:** `IMemoryCache` (PoC) → Redis com `IDistributedCache` (Produção). Interface `IConsolidadoCache` abstrai a implementação.

**Contexto:** 50 rps = 50 × 60 × 60 = **180.000 queries/hora** ao banco sem cache. Inviável.

| Cenário | Latência | Capacidade |
|---|---|---|
| Sem cache (banco direto) | ~50ms | ~200 rps (com pool saturado) |
| Com cache (hit) | < 5ms | > 10.000 rps |
| Com cache (miss) | ~55ms | Raramente ocorre |

**Invalidação:** Consumer invalida o cache sempre que um evento chega, garantindo que a próxima leitura reflita o estado atual.

---

### ADR-004 — .NET 9 + Minimal API + CQRS

**Decisão:** ASP.NET Core 9 Minimal API com MediatR 12 e FluentValidation 11.

| Componente | Justificativa |
|---|---|
| **.NET 9** | ~15% menos alocações HTTP vs .NET 8; melhor startup em containers |
| **Minimal API** | Menor overhead de reflection vs MVC Controller; ideal para microsserviços |
| **MediatR** | Pipeline behaviors para Validation + Logging sem poluir handlers |
| **FluentValidation** | Validações expressivas e testáveis separadas da lógica de negócio |
| **Result<T>** | Erros como valores — sem exceptions para controle de fluxo |

---

### ADR-005 — SQLite (PoC) → PostgreSQL (Produção)

**Decisão:** SQLite para desenvolvimento/avaliação sem infra; PostgreSQL em produção.

A troca é **1 linha no `Program.cs`**:
```csharp
// PoC:   opts.UseSqlite(connStr)
// Prod:  opts.UseNpgsql(connStr)
```

Mesmas migrations do EF Core funcionam nos dois providers.

---

## Arquitetura Alvo

### Visão Geral (C4 — Nível de Contêiner)

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  CLIENTE (Web / App / curl)                                                  │
└──────────────────────────────────┬───────────────────────────────────────────┘
                                   │ HTTPS
┌──────────────────────────────────▼───────────────────────────────────────────┐
│  API GATEWAY                                                                 │
│  · Rate Limiting  · JWT Validation  · TLS 1.3  · CORS  · mTLS interno       │
└─────────────────────┬─────────────────────────────┬──────────────────────────┘
                      │                             │
         ┌────────────▼───────────┐    ┌────────────▼────────────────────────┐
         │   lancamentos-api       │    │   consolidado-api                   │
         │   :5001                 │    │   :5002                             │
         │                         │    │                                     │
         │  ASP.NET Core 9         │    │  ASP.NET Core 9                     │
         │  CQRS · DDD · Outbox    │    │  CQRS (read) · Cache · Consumer     │
         │                         │    │                                     │
         │  ┌─────────────────┐    │    │  ┌───────────┐  ┌───────────────┐  │
         │  │   PostgreSQL     │    │    │  │   Redis   │  │  PostgreSQL   │  │
         │  │   lancamentos   │    │    │  │  TTL 30s  │  │  consolidado  │  │
         │  │   outbox_msgs   │    │    │  └───────────┘  └───────────────┘  │
         │  └────────┬────────┘    │    └────────────────────────▲───────────┘
         │           │ Outbox      │                             │
         │           │ Relay (2s)  │                             │ consume evento
         └───────────┼─────────────┘                            │
                     │ publica evento                            │
         ┌───────────▼─────────────────────────────────────────┘
         │   Message Broker                                      │
         │   Azure Service Bus (Produção)                        │
         │   RabbitMQ (Desenvolvimento)                          │
         │   InMemoryMessageBus (PoC)                            │
         └───────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────────────┐
│  OBSERVABILIDADE                                                             │
│  OpenTelemetry (Traces) · Prometheus (Métricas) · Grafana · Serilog (Logs)  │
└──────────────────────────────────────────────────────────────────────────────┘
```

### Camadas Internas (Clean Architecture)

```
┌─────────────────────────────────────────────────────────────────┐
│  API Layer          Program.cs · Endpoints · Middleware          │
├─────────────────────────────────────────────────────────────────┤
│  Application Layer  Commands · Queries · Handlers · Validators  │
│                     Pipeline Behaviors (Validation + Logging)   │
├─────────────────────────────────────────────────────────────────┤
│  Domain Layer       Entities · Events · Exceptions · Interfaces │
│                     ← ZERO dependências externas →              │
├─────────────────────────────────────────────────────────────────┤
│  Infrastructure     EF Core · Repositories · Outbox · Cache     │
│  Layer              MessageBus · Consumers · BackgroundServices  │
└─────────────────────────────────────────────────────────────────┘
        Regra: dependências só apontam para dentro (DIP)
```

---

## Fluxos de Dados Críticos

### Fluxo 1 — Registrar Lançamento

```
Cliente
  │  POST /api/lancamentos {valor, tipo, descricao, data}
  ▼
API Gateway (JWT validation + Rate Limiting)
  │
  ▼
ValidationBehavior → FluentValidation
  │  ✅ válido
  ▼
RegistrarLancamentoHandler
  │
  ├─ Lancamento.Criar()            ← invariantes no domínio
  │   · valor > 0
  │   · descrição obrigatória
  │   · sem data futura
  │
  ├─ repository.AdicionarAsync()   ┐
  ├─ outbox.AdicionarAsync()       ├─ MESMA TRANSAÇÃO (atomicidade)
  └─ unitOfWork.CommitAsync()      ┘
  │
  ▼
201 Created {id: "..."}

[~2 segundos depois — assíncrono]

OutboxRelayWorker
  │  lê outbox_messages pendentes
  ▼
messageBus.PublicarAsync(LancamentoRegistradoEvent)
  │
  ▼
LancamentoEventConsumer (consolidado-api)
  ├─ consolidado.AplicarLancamento(valor, tipo)
  ├─ repository.SalvarAsync(consolidado)
  └─ cache.InvalidarAsync(data)        ← próxima leitura vai ao banco
```

### Fluxo 2 — Consultar Consolidado (50 rps)

```
Cliente
  │  GET /api/consolidado/2025-01-15
  ▼
ObterConsolidadoDiarioHandler
  │
  ├─ cache.ObterAsync(data)
  │   │
  │   ├─ HIT  → retorna DTO em < 5ms   ✅  (99% das requisições)
  │   │
  │   └─ MISS → repository.ObterPorDataAsync(data)
  │               │
  │               ├─ Atualiza cache TTL 30s
  │               └─ retorna em ~55ms   ✅
  │
  ▼
200 OK {data, totalCreditos, totalDebitos, saldoLiquido, atualizadoEm}

Capacidade:
  Com cache HIT:  50 rps → 50 × 0.005s = 0.25s de thread total/s
  Sem cache:      50 rps → 50 × 0.055s = 2.75s → connection pool sob pressão
  Cache resolve o problema estruturalmente.
```

---

## Estrutura do Projeto

```
FluxoCaixa/
│
├── FluxoCaixa.sln
│
├── src/
│   ├── FluxoCaixa.SharedKernel/           # IDomainEvent · Result<T>
│   │
│   ├── FluxoCaixa.Lancamentos.Domain/     # Entidade rica · Events · Exceptions
│   ├── FluxoCaixa.Lancamentos.Application/ # CQRS: Commands · Queries · Behaviors
│   ├── FluxoCaixa.Lancamentos.Infrastructure/ # EF Core · Outbox · MessageBus
│   └── FluxoCaixa.Lancamentos.API/        # Minimal API · Program.cs
│
│   ├── FluxoCaixa.Consolidado.Domain/     # ConsolidadoDiario · Repository interface
│   ├── FluxoCaixa.Consolidado.Application/ # Queries · IConsolidadoCache
│   ├── FluxoCaixa.Consolidado.Infrastructure/ # EF Core · Redis · Consumer
│   └── FluxoCaixa.Consolidado.API/        # Minimal API · Program.cs
│
├── tests/
│   ├── FluxoCaixa.Lancamentos.UnitTests/  # Domínio (9) + Application (5)
│   ├── FluxoCaixa.Consolidado.UnitTests/  # Domínio (8) + Application (5)
│   └── FluxoCaixa.IntegrationTests/       # E2E com WebApplicationFactory (7)
│
├── docker/
│   ├── lancamentos.Dockerfile             # Multi-stage build
│   └── consolidado.Dockerfile
│
├── docker-compose.yml
│
└── docs/adr/
    ├── ADR-001-microsservicos.md
    ├── ADR-002-outbox-pattern.md
    └── ADR-003-004-cache-stack.md
```

---

## Como Rodar Localmente

### Pré-requisitos

| Ferramenta | Versão | Download |
|---|---|---|
| .NET SDK | **9.0** | https://dot.net/download |
| Docker Desktop | 4.x (opcional) | https://docker.com |
| Git | qualquer | https://git-scm.com |

### Opção A — Direto com .NET (mais rápido para avaliação)

```bash
# 1. Clone
git clone https://github.com/seu-usuario/fluxocaixa.git
cd fluxocaixa

# 2. Terminal 1 — Lançamentos API
cd src/FluxoCaixa.Lancamentos.API
dotnet run
# ✅ http://localhost:5001/swagger

# 3. Terminal 2 — Consolidado API
cd src/FluxoCaixa.Consolidado.API
dotnet run
# ✅ http://localhost:5002/swagger
```

> Os bancos SQLite (`lancamentos.db`, `consolidado.db`) são criados e migrados automaticamente.

### Opção B — Docker Compose

```bash
docker compose up --build
# Lançamentos: http://localhost:5001/swagger
# Consolidado: http://localhost:5002/swagger
```

### Teste Rápido (fluxo completo)

```bash
# 1. Registrar crédito
curl -X POST http://localhost:5001/api/lancamentos \
  -H "Content-Type: application/json" \
  -d '{"valor": 1500.00, "tipo": 2, "descricao": "Venda do dia", "data": "2025-01-15"}'
# → 201 Created {"id": "..."}

# 2. Registrar débito
curl -X POST http://localhost:5001/api/lancamentos \
  -H "Content-Type: application/json" \
  -d '{"valor": 300.00, "tipo": 1, "descricao": "Aluguel", "data": "2025-01-15"}'

# 3. Aguardar ~2s para o Outbox processar
sleep 2

# 4. Consultar consolidado
curl http://localhost:5002/api/consolidado/2025-01-15
# → {"totalCreditos": 1500.00, "totalDebitos": 300.00, "saldoLiquido": 1200.00}

# 5. Listar lançamentos do dia
curl http://localhost:5001/api/lancamentos/2025-01-15

# 6. Período
curl "http://localhost:5002/api/consolidado/periodo?inicio=2025-01-01&fim=2025-01-31"
```

---

## Endpoints da API

### Serviço de Lançamentos — porta 5001

| Método | Endpoint | Descrição | Response |
|---|---|---|---|
| `POST` | `/api/lancamentos` | Registra novo lançamento | `201 Created {id}` |
| `GET` | `/api/lancamentos/{data}` | Lista lançamentos de uma data (`yyyy-MM-dd`) | `200 OK [{...}]` |
| `DELETE` | `/api/lancamentos/{id}` | Cancela um lançamento | `204 No Content` |
| `GET` | `/health` | Health check | `200 Healthy` |

**Body do POST:**
```json
{
  "valor": 750.50,
  "tipo": 2,
  "descricao": "Recebimento NF 1234",
  "data": "2025-01-15"
}
```
`tipo`: `1` = Débito · `2` = Crédito

**Validações:**

| Campo | Regra |
|---|---|
| `valor` | > 0 e ≤ 10.000.000 |
| `tipo` | 1 ou 2 |
| `descricao` | Obrigatória, máx. 255 caracteres |
| `data` | Não pode ser data futura |

### Serviço de Consolidado — porta 5002

| Método | Endpoint | Descrição |
|---|---|---|
| `GET` | `/api/consolidado/{data}` | Saldo consolidado de um dia (`yyyy-MM-dd`) |
| `GET` | `/api/consolidado/periodo?inicio={data}&fim={data}` | Consolidado por período (máx. 365 dias) |
| `GET` | `/health` | Health check |

**Response consolidado:**
```json
{
  "data": "2025-01-15",
  "totalCreditos": 1500.00,
  "totalDebitos": 300.00,
  "saldoLiquido": 1200.00,
  "atualizadoEm": "2025-01-15T10:32:15.123Z"
}
```

---

## Testes

```bash
# Todos os testes
dotnet test

# Por suite
dotnet test tests/FluxoCaixa.Lancamentos.UnitTests/
dotnet test tests/FluxoCaixa.Consolidado.UnitTests/
dotnet test tests/FluxoCaixa.IntegrationTests/

# Com cobertura
dotnet test --collect:"XPlat Code Coverage"
```

### Suítes e Estratégia

```
Pirâmide de Testes:

        ┌──────────────────────────────┐
        │  Integration Tests  (7)      │  WebApplicationFactory + SQLite In-Memory
        │  Testa o sistema real E2E    │  HTTP: 201, 400, 404, 204, health
        └──────────────────────────────┘
        ┌──────────────────────────────┐
        │  Application Tests  (10)     │  Handlers com mocks (NSubstitute)
        │  Commands + Queries          │  Validators + Pipeline behaviors
        └──────────────────────────────┘
        ┌──────────────────────────────┐
        │  Domain Tests  (17)          │  Puras, sem mocks
        │  Regras de negócio           │  Criação, cancelamento, cálculo de saldo
        └──────────────────────────────┘
```

| Suite | Casos | O que cobre |
|---|---|---|
| `Lancamentos.UnitTests.Domain` | 9 | Criação com dados válidos/inválidos, eventos publicados, cancelamento |
| `Lancamentos.UnitTests.Application` | 5 | Handler persistência, Validator todas as regras |
| `Consolidado.UnitTests.Domain` | 8 | Cálculo de saldo, estorno, valores negativos |
| `Consolidado.UnitTests.Application` | 5 | Cache HIT/MISS/zero, período inválido, >365 dias |
| `IntegrationTests` | 7 | POST 201/400, GET listagem, DELETE 204/404, health |

---

## Segurança

### Autenticação e Autorização

- **JWT Bearer Token** em todos os endpoints (`/health` e `/metrics` excluídos)
- **Scopes**: `lancamentos:write`, `lancamentos:read`, `consolidado:read`
- **TTL**: Access Token 15 min + Refresh Token 7 dias com rotation

### Comunicação entre Serviços

- **mTLS** entre serviços via Service Mesh (Linkerd/Istio em Kubernetes)
- **Credenciais do broker** via Azure Key Vault — nunca em `appsettings.json`
- **Connection strings** injetadas por variáveis de ambiente em runtime

### Rate Limiting (.NET 9 nativo)

```csharp
builder.Services.AddRateLimiter(opts =>
    opts.AddFixedWindowLimiter("lancamentos", l => {
        l.PermitLimit = 100;
        l.Window = TimeSpan.FromMinutes(1);
        l.QueueLimit = 10;
    }));
```

### OWASP Top 10 — Mitigações

| Risco | Mitigação |
|---|---|
| A01 Broken Access Control | JWT + Scopes + Policies por endpoint |
| A02 Cryptographic Failures | HTTPS obrigatório, TLS 1.3, dados em repouso criptografados |
| A03 Injection | EF Core parameterized queries por padrão; zero SQL concatenado |
| A04 Insecure Design | DDD com domínio isolado; sem lógica de negócio na API layer |
| A07 Auth Failures | JWT com expiração curta + Refresh Token rotation |
| A09 Logging Failures | Serilog estruturado; sem log de dados sensíveis (PII mascarado) |

---

## Monitoramento & Observabilidade

### Os Três Pilares

**1. Logs Estruturados (Serilog + OpenTelemetry)**
```json
{
  "Timestamp": "2025-01-15T10:30:00Z",
  "Level": "Information",
  "Message": "Lançamento registrado",
  "Service": "lancamentos-api",
  "CorrelationId": "abc-123",
  "TraceId": "def-456",
  "LancamentoId": "guid-aqui",
  "Tipo": "Credito",
  "Valor": 1500.00,
  "DurationMs": 45
}
```

**2. Métricas (Prometheus + Grafana)**
```csharp
// Métricas customizadas de negócio
var lancamentosRegistrados = meter.CreateCounter<long>("lancamentos_registrados_total");
var outboxPendentes = meter.CreateObservableGauge<int>("outbox_mensagens_pendentes",
    () => outboxRepo.ContarPendentesAsync().Result);
var cacheHitRate = meter.CreateObservableGauge<double>("consolidado_cache_hit_rate");
```

**3. Distributed Tracing (Jaeger)**
Trace completo: `API Gateway → lancamentos-api → PostgreSQL → Outbox → Broker → consolidado-api → Redis`

### Health Checks

```csharp
app.MapHealthChecks("/health/live");          // Liveness: app está de pé?
app.MapHealthChecks("/health/ready",          // Readiness: banco + broker OK?
    new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
```

### Alertas Críticos

| Alerta | Condição | Severidade |
|---|---|---|
| Alta latência no consolidado | P99 > 500ms por 2min | ⚠️ Warning |
| Taxa de erros elevada | > 5% de 5xx em 5min | 🔴 Critical |
| Outbox travada | Pendentes > 500 por 5min | 🔴 Critical |
| Serviço indisponível | Health check failing | 🔴 Critical |
| Redis down | Connection refused | ⚠️ Warning |

---

## Arquitetura de Transição

### Cenário: Migração de Sistema Legado

Usando o **Strangler Fig Pattern** para migração sem downtime:

```
FASE 1 — Coexistência (Meses 1–3)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
API Gateway roteia:
  /api/lancamentos  ──→  Novo sistema (lancamentos-api)
  /relatorios/*     ──→  Sistema legado (somente leitura)

ETL incremental: migra histórico de lançamentos em batches noturnos.
Zero downtime para o comerciante.

FASE 2 — Shadow Mode (Meses 3–4)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Dual-write: escreve nos dois sistemas.
Job diário compara totais entre legado e novo sistema.
Log de divergências para auditoria e correção.

FASE 3 — Descomissionamento (Mês 5+)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
100% do tráfego no novo sistema.
Legado em modo read-only por 90 dias (auditoria fiscal).
Desligamento após validação.
```

### ETL de Migração

```csharp
// Job executado como BackgroundService durante a transição
// Lê lançamentos legados não migrados → transforma para modelo canônico
// → POST na nova API (respeitando domínio) → marca como migrado
// → Log de auditoria com hash de comparação de totais
```

---

## Estimativa de Custos Azure

### Ambiente de Produção (Brazil South)

| Serviço | Configuração | USD/mês |
|---|---|---|
| Container Apps — Lançamentos | 2 réplicas · 0.5 vCPU · 1GB | ~$40 |
| Container Apps — Consolidado | 3 réplicas · 0.5 vCPU · 1GB | ~$60 |
| Azure Database for PostgreSQL Flexible | Burstable B2s · 2 vCores · 4GB | ~$50 |
| Azure Cache for Redis | C1 Standard · 1GB | ~$55 |
| Azure Service Bus | Standard · < 10M mensagens/mês | ~$10 |
| Azure Container Registry | Basic · 10GB | ~$5 |
| Azure Monitor + Log Analytics | 5GB/dia | ~$25 |
| **Total estimado** | | **~$245/mês** |

> **MVP / Startup:** Container Apps com scale-to-zero → ~**$80/mês** em off-peak.  
> **Alta disponibilidade:** Adicionar réplicas e zone redundancy → ~**$420/mês**.

---

## PoC vs Produção

A PoC funciona com zero dependências externas. Cada componente tem troca por configuração:

| Componente | PoC (este repo) | Produção | Troca |
|---|---|---|---|
| Banco de Dados | SQLite | PostgreSQL 16 | 1 linha: `UseNpgsql()` |
| Mensageria | InMemoryMessageBus | Azure Service Bus | DI config: `IMessageBus` |
| Cache | IMemoryCache | Redis 7 | DI config: `IConsolidadoCache` |
| Autenticação | Sem auth | JWT + Azure AD | Middleware no `Program.cs` |
| Containers | Docker Compose | AKS (Kubernetes) | Helm charts |
| Secrets | appsettings.json | Azure Key Vault | `AddAzureKeyVault()` |
| CI/CD | — | GitHub Actions | `.github/workflows/` |
| Observabilidade | Console logs | OTel + Jaeger + Grafana | `AddOpenTelemetry()` |

---

## Evoluções Futuras

**Arquiteturais**
- Event Sourcing completo com EventStoreDB para histórico imutável
- CQRS com read replica PostgreSQL dedicada ao consolidado
- Pact.NET — Contract Testing entre os dois serviços
- Chaos Engineering com Simmy para validar resiliência

**Funcionais**
- Múltiplas contas caixa por comerciante (multi-tenancy)
- Categorização de lançamentos (tags para relatórios analíticos)
- Exportação do consolidado em PDF/Excel
- Notificações (e-mail/webhook) quando saldo fica negativo
- Conciliação bancária via importação de OFX

**Operacionais**
- GitHub Actions: build → test → coverage gate (80%) → SAST → deploy
- Terraform IaC para provisionar toda a infraestrutura Azure
- Blue/Green deployment para zero-downtime releases

---

## Tecnologias

| Tecnologia | Versão | Uso |
|---|---|---|
| .NET / C# | 9.0 | Runtime e linguagem |
| ASP.NET Core | 9.0 | Minimal API · DI · Health Checks |
| MediatR | 12.x | CQRS · Pipeline Behaviors |
| FluentValidation | 11.x | Validação declarativa de commands |
| Entity Framework Core | 9.x | ORM · Migrations |
| SQLite / PostgreSQL | 3.x / 16 | Banco PoC / Produção |
| xUnit + FluentAssertions | 2.9.x / 6.x | Test runner + assertions legíveis |
| NSubstitute | 5.x | Mocking de contratos de infra |
| Docker + Compose | 24.x | Containerização local |
| OpenTelemetry | — | Distributed tracing + métricas |

---

*Banco Carrefour — Desafio Arquiteto de Soluções*
