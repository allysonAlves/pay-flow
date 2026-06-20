# PayFlow — Payment Processing API

API de processamento de pagamentos em .NET 8 demonstrando a aplicação de DDD, Clean Architecture, CQRS e Outbox Pattern em um domínio financeiro realista.

## Stack

- **.NET 8** — runtime e framework web
- **PostgreSQL 16** — persistência principal
- **Redis 7** — controle de idempotência
- **EF Core 8** — ORM e migrations
- **MediatR 14** — dispatcher de Commands e Queries
- **FluentValidation 12** — validação de commands
- **Docker / Docker Compose** — infraestrutura local

---

## Padrões e Conceitos Aplicados

| Padrão | Onde | Como |
|---|---|---|
| **Domain-Driven Design** | `PayFlow.Domain` | `Payment` como Aggregate Root; Value Objects tipados (`Money`, `PaymentId`); Domain Events levantados nos métodos do agregado |
| **Clean Architecture** | 4 projetos | Dependências fluem de fora para dentro; Domain não depende de ninguém |
| **CQRS** | `PayFlow.Application` | Commands (escrita via agregado) separados de Queries (leitura via DTOs com `AsNoTracking`) |
| **MediatR** | Application + API | Dispatcher para Commands e Queries; Pipeline Behavior para validação automática |
| **Outbox Pattern** | `PayFlow.Infrastructure` | `OutboxMessage` persiste na mesma transação do agregado; `OutboxProcessor` processa assincronamente |
| **Domain Events vs Integration Events** | Infrastructure | Domain Events = in-memory no agregado; Integration Events = serializados no Outbox para consumo posterior |
| **Idempotência** | Infrastructure | Redis previne reprocessamento de eventos duplicados no `OutboxProcessor` |
| **FluentValidation** | Application | Validators por Command; `ValidationBehavior` como MediatR pipeline intercepta antes dos handlers |
| **API Key Auth** | `PayFlow.API/Middleware` | `ApiKeyMiddleware` valida header `X-Api-Key` com comparação constant-time em todos os endpoints |
| **Webhook HMAC-SHA256** | `PayFlow.API/Filters` | `WebhookSignatureFilter` valida assinatura `X-Webhook-Signature` antes de processar callbacks do gateway |

---

## Arquitetura

```
┌─────────────────────────────────────────────────────────┐
│                      PayFlow.API                        │
│          Controllers · Middleware · Program.cs           │
└──────────────────────┬──────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────┐
│                  PayFlow.Application                    │
│     Commands · Queries · DTOs · Validators · Behaviors   │
└──────────────────────┬──────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────┐
│                   PayFlow.Domain                        │
│      Aggregate · Value Objects · Domain Events           │
│              Interfaces (ports)                          │
└─────────────────────────────────────────────────────────┘
                       ▲
┌──────────────────────┴──────────────────────────────────┐
│                PayFlow.Infrastructure                   │
│   EF Core · PostgreSQL · Redis · Outbox · FakeGateway   │
└─────────────────────────────────────────────────────────┘
```

**Regra de dependência:** cada camada conhece apenas a camada abaixo. Infrastructure implementa as interfaces declaradas em Domain e Application.

---

## Ciclo de Vida de um Pagamento

```
Pending ──► Processing ──► Approved
   │              │
   │              └──────► Failed
   │
   └──────────────────────► Cancelled
Processing ────────────────► Cancelled
```

---

## Fluxo Principal — Criar e Processar um Pagamento

```
1. POST /payments
      │
      ▼
   CreatePaymentCommandHandler
      ├── Payment.Create(...)           → levanta PaymentCreated
      ├── IPaymentRepository.AddAsync
      ├── IOutboxPublisher.PublishAsync → salva OutboxMessage no banco
      └── IUnitOfWork.CommitAsync       → transação única (Payment + OutboxMessage)
      │
      ▼
   201 Created { paymentId }

2. OutboxProcessor (background, a cada 10s)
      ├── lê OutboxMessages não processados
      ├── verifica idempotência no Redis
      └── MediatR.Send(ProcessPaymentCommand)
            ├── payment.StartProcessing()
            ├── IPaymentGateway.ProcessAsync()   → FakePaymentGateway
            ├── payment.Approve() ou Fail()
            └── IUnitOfWork.CommitAsync()
```

---

## Endpoints

| Método | Rota | Descrição |
|---|---|---|
| `POST` | `/payments` | Cria um pagamento (status inicial: Pending) |
| `GET` | `/payments/{id}` | Retorna detalhes de um pagamento |
| `GET` | `/payments` | Lista pagamentos com paginação e filtro por status |
| `POST` | `/payments/{id}/cancel` | Cancela um pagamento |
| `POST` | `/payments/webhook` | Simula callback do gateway externo |

### POST /payments

```json
// Request
{
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "merchantId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "amount": 150.00,
  "currency": "BRL"
}

// Response 201
{
  "paymentId": "a3bb189e-8bf9-3888-9912-ace4e6543002"
}
```

### GET /payments/{id}

```json
// Response 200
{
  "id": "a3bb189e-8bf9-3888-9912-ace4e6543002",
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "merchantId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "amount": 150.00,
  "currency": "BRL",
  "status": "Approved",
  "createdAt": "2026-06-12T18:00:00Z",
  "updatedAt": "2026-06-12T18:00:10Z"
}
```

### GET /payments

```
GET /payments?status=Pending&page=1&pageSize=20
```

### POST /payments/webhook

```json
// Simula aprovação pelo gateway
{
  "paymentId": "a3bb189e-8bf9-3888-9912-ace4e6543002",
  "success": true,
  "transactionId": "txn_abc123"
}

// Simula falha pelo gateway
{
  "paymentId": "a3bb189e-8bf9-3888-9912-ace4e6543002",
  "success": false,
  "errorMessage": "Card declined"
}
```

---

## Estrutura de Pastas

```
pay-flow/
├── src/
│   ├── PayFlow.Domain/
│   │   ├── Aggregates/Payments/
│   │   │   ├── Payment.cs
│   │   │   ├── PaymentStatus.cs
│   │   │   └── Events/
│   │   │       ├── DomainEvent.cs (base)
│   │   │       ├── PaymentCreated.cs
│   │   │       ├── PaymentProcessingStarted.cs
│   │   │       ├── PaymentApproved.cs
│   │   │       ├── PaymentFailed.cs
│   │   │       └── PaymentCancelled.cs
│   │   ├── ValueObjects/
│   │   │   ├── PaymentId.cs
│   │   │   ├── CustomerId.cs
│   │   │   ├── MerchantId.cs
│   │   │   └── Money.cs
│   │   ├── Exceptions/DomainException.cs
│   │   └── Interfaces/
│   │       ├── IPaymentRepository.cs
│   │       └── IUnitOfWork.cs
│   │
│   ├── PayFlow.Application/
│   │   ├── Common/
│   │   │   ├── PagedResult.cs
│   │   │   └── ValidationBehavior.cs
│   │   ├── Contracts/
│   │   │   ├── GatewayRequest.cs
│   │   │   └── GatewayResponse.cs
│   │   ├── Interfaces/
│   │   │   ├── IPaymentGateway.cs
│   │   │   ├── IPaymentReadRepository.cs
│   │   │   ├── IOutboxPublisher.cs
│   │   │   └── IIdempotencyService.cs
│   │   ├── Payments/
│   │   │   ├── Commands/
│   │   │   │   ├── CreatePayment/
│   │   │   │   ├── CancelPayment/
│   │   │   │   ├── ProcessPayment/
│   │   │   │   └── Webhook/
│   │   │   ├── Queries/
│   │   │   │   ├── GetPayment/
│   │   │   │   └── ListPayments/
│   │   │   └── DTOs/
│   │   └── DependencyInjection.cs
│   │
│   ├── PayFlow.Infrastructure/
│   │   ├── Gateway/
│   │   │   ├── FakePaymentGateway.cs
│   │   │   └── FakeGatewayOptions.cs
│   │   ├── Idempotency/RedisIdempotencyService.cs
│   │   ├── Outbox/
│   │   │   ├── OutboxMessage.cs
│   │   │   ├── OutboxOptions.cs
│   │   │   ├── OutboxPublisher.cs
│   │   │   └── OutboxProcessor.cs
│   │   ├── Persistence/
│   │   │   ├── Configurations/
│   │   │   ├── Migrations/
│   │   │   ├── Repositories/
│   │   │   ├── PaymentDbContext.cs
│   │   │   └── UnitOfWork.cs
│   │   └── DependencyInjection.cs
│   │
│   └── PayFlow.API/
│       ├── Controllers/PaymentsController.cs
│       ├── Filters/WebhookSignatureFilter.cs
│       ├── Middleware/ApiKeyMiddleware.cs
│       ├── Middleware/GlobalExceptionHandler.cs
│       ├── Requests/CreatePaymentRequest.cs
│       ├── Program.cs
│       └── appsettings.json
│
├── tests/
│   ├── PayFlow.Domain.Tests/
│   ├── PayFlow.Application.Tests/
│   ├── PayFlow.Infrastructure.Tests/
│   └── PayFlow.API.Tests/
│
├── docs/superpowers/specs/
├── docker-compose.yml
└── PayFlow.sln
```

---

## Como Rodar

### Pré-requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop)

### 1. Subir a infraestrutura

```bash
docker compose up -d
```

### 2. Aplicar migrations

```bash
dotnet ef database update \
  --project src/PayFlow.Infrastructure \
  --startup-project src/PayFlow.API
```

### 3. Rodar a API

```bash
dotnet run --project src/PayFlow.API
```

A API estará disponível em `http://localhost:5000`.  
Swagger UI: `http://localhost:5000/swagger`

### 4. Rodar os testes

```bash
dotnet test
```

---

## Criar migrations (quando alterar o modelo)

```bash
dotnet ef migrations add <NomeDaMigration> \
  --project src/PayFlow.Infrastructure \
  --startup-project src/PayFlow.API
```

---

## Testando o Fluxo via curl

```bash
API_KEY="dev-api-key-change-in-production"

# 1. Criar pagamento
curl -s -X POST http://localhost:5000/payments \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: $API_KEY" \
  -d '{"customerId":"3fa85f64-5717-4562-b3fc-2c963f66afa6","merchantId":"7c9e6679-7425-40de-944b-e07fc1f90ae7","amount":150.00,"currency":"BRL"}' | jq

# 2. Consultar pagamento (substitua o ID)
curl -s -H "X-Api-Key: $API_KEY" http://localhost:5000/payments/{id} | jq

# 3. Aguardar ~10s para o OutboxProcessor processar, depois consultar novamente
# Status deve estar Approved (amount < 10000) ou Failed (amount >= 10000)

# 4. Cancelar pagamento (funciona em Pending ou Processing)
curl -s -X POST -H "X-Api-Key: $API_KEY" http://localhost:5000/payments/{id}/cancel

# 5. Webhook simulado com assinatura HMAC-SHA256
BODY='{"paymentId":"{id}","success":true,"transactionId":"txn_test_001"}'
SECRET="dev-webhook-secret-change-in-production"
SIG=$(echo -n "$BODY" | openssl dgst -sha256 -hmac "$SECRET" | awk '{print $2}')
curl -s -X POST http://localhost:5000/payments/webhook \
  -H "Content-Type: application/json" \
  -H "X-Webhook-Signature: $SIG" \
  -d "$BODY"

# 6. Listar pagamentos com filtro
curl -s -H "X-Api-Key: $API_KEY" "http://localhost:5000/payments?status=Approved&page=1&pageSize=10" | jq
```

---

## Configuração do FakeGateway

O gateway de pagamento é simulado. O comportamento é configurável em `appsettings.json`:

```json
"FakeGateway": {
  "FailureThreshold": 10000,
  "DelayMs": 500
}
```

- **FailureThreshold:** pagamentos com `amount >= 10000` são rejeitados
- **DelayMs:** simula latência de rede do gateway

Para integrar com um gateway real (ex: Stripe), implemente `IPaymentGateway` e troque o registro em `DependencyInjection.cs`.

---

## Testes

```
tests/
├── PayFlow.Domain.Tests/          # 13 testes
├── PayFlow.Application.Tests/     # 6 testes
├── PayFlow.Infrastructure.Tests/  # 6 testes
└── PayFlow.API.Tests/             # 8 testes (middleware e filter de segurança)
```

**Domain.Tests** — testa o agregado `Payment` de forma isolada (sem dependências externas):
- Máquina de estados: transições válidas (`Pending → Processing → Approved/Failed`, `Pending → Cancelled`) e inválidas (ex: aprovar um pagamento já cancelado lança `DomainException`)
- Levantamento de Domain Events: cada método do agregado levanta o evento esperado com os dados corretos
- Value Objects: criação inválida de `Money` (valor negativo, currency vazia) rejeita na construção

**Application.Tests** — testa os handlers de comando com portas mockadas (NSubstitute):
- `CreatePaymentCommandHandler`: persiste o agregado e publica no outbox na mesma unidade de trabalho
- `CancelPaymentCommandHandler`: delega o cancelamento ao agregado e confirma transação
- `GetPaymentQueryHandler`: propaga `KeyNotFoundException` quando o pagamento não existe

**Infrastructure.Tests** — testa o `OutboxProcessor` com `FakeOutboxStore` (implementação in-memory de `IOutboxStore`):
- Caminho feliz: `PaymentCreated` e `PaymentApproved` disparam os Integration Events corretos via MediatR
- Idempotência: mensagem já marcada no Redis é ignorada sem publicação duplicada
- Retry: falha no `mediator.Publish` incrementa `RetryCount` sem marcar `ProcessedAt`
- Dead-letter: mensagem que atinge `MaxRetries` (5) recebe `ProcessedAt` e para de ser reprocessada
- Tipo desconhecido: `EventType` sem mapeamento é marcado como processado sem publicação

A estratégia de extrair `IOutboxStore` (interface `internal`) permite testar o `OutboxProcessor` sem depender de PostgreSQL ou do comando SQL `FOR UPDATE SKIP LOCKED`, que é incompatível com providers in-memory.

---

## Considerações de Produção

Este projeto foi desenvolvido como portfólio e demonstração de arquitetura. Para uma implantação real, os pontos abaixo precisariam ser endereçados:

**Segurança**
- **Autenticação implementada:** todos os endpoints exigem `X-Api-Key`; o webhook valida `X-Webhook-Signature` com HMAC-SHA256. Em produção, a chave viria de um secret manager, não do `appsettings.json`.
- **HTTPS obrigatório:** `UseHttpsRedirection` está ativo. Em produção, desabilitar HTTP completamente ou forçar redirecionamento via reverse proxy (nginx/Caddy).

**Observabilidade**
- **Structured logging:** trocar o logger padrão por Serilog com sinks configuráveis (Console JSON, Seq, Datadog).
- **Tracing distribuído:** adicionar OpenTelemetry para correlacionar requests HTTP com processamentos do `OutboxProcessor`.
- **Métricas:** expor contadores de eventos processados, falhas e dead-letters via `System.Diagnostics.Metrics`.

**Infraestrutura**
- **Secrets management:** connection strings e credenciais devem vir de variáveis de ambiente ou cofre (AWS Secrets Manager, Azure Key Vault, HashiCorp Vault) — nunca de `appsettings.json` em produção.
- **Múltiplas instâncias:** o `OutboxProcessor` usa `FOR UPDATE SKIP LOCKED` para garantir que duas instâncias da API não processem a mesma mensagem.
- **Gateway real:** implementar `IPaymentGateway` com o SDK do gateway escolhido (Stripe, Adyen, etc.) e registrá-lo no `DependencyInjection.cs`.

---

## Decisões de Design Notáveis

**Por que `IPaymentGateway` fica em Application e não em Domain?**  
Quem chama o gateway é o `ProcessPaymentCommandHandler` (Application), não o agregado. O Domain não deve conhecer gateways externos. Portas de orquestração ficam em Application; portas de persistência do agregado (`IPaymentRepository`) ficam em Domain.

**Por que o Outbox Publisher é chamado no Handler e não no UnitOfWork?**  
Torna o fluxo explícito — o handler declara que está publicando eventos. Se estivesse no UnitOfWork, o comportamento seria implícito e mais difícil de rastrear.

**Por que BackgroundService usa `IServiceScopeFactory`?**  
`BackgroundService` é registrado como Singleton. `DbContext` e repositórios são Scoped. O `OutboxProcessor` cria um scope por iteração para não manter um DbContext vivo indefinidamente.

**Por que `PaymentReadRepository` é separado de `PaymentRepository`?**  
Leitura usa `AsNoTracking()` e retorna DTOs — nunca instancia o agregado completo. A separação elimina o risco de alguém usar o repositório de leitura para escrita, e deixa explícito o modelo de dois lados do CQRS.

**Por que `IOutboxStore` é `internal` e não `public`?**  
É um detalhe de implementação do módulo Infrastructure — `EfOutboxStore` usa SQL específico do PostgreSQL (`FOR UPDATE SKIP LOCKED`) que não faz sentido expor além da camada. O acesso para testes é garantido via `InternalsVisibleTo` no `.csproj`, sem vazar a abstração para fora do assembly.
