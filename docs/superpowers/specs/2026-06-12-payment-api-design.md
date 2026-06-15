# Payment Processing API — Spec de Design

**Data:** 2026-06-12
**Stack:** .NET 8, C#, PostgreSQL, Redis, Docker
**Objetivo:** Projeto de estudo — DDD, Clean Architecture, CQRS, Outbox Pattern, Idempotência

---

## Decisões de Design

| Questão | Decisão | Motivo |
|---|---|---|
| Gateway externo | `IPaymentGateway` + `FakePaymentGateway` | Pratica inversão de dependência sem distração de SDK externo |
| Escopo do domínio | Apenas `Payment`; `CustomerId`/`MerchantId` como VOs tipados | Foco nos padrões, sem modelagem de entidades secundárias |
| Processamento | Assíncrono via Outbox Pattern | Exercita o padrão de forma significativa e realista |
| Queries | Read models com DTOs separados do agregado | Separação write/read sem complexidade de dois datastores |
| Endpoints | Create, Detail, List, Cancel, Webhook simulado | Fecha o ciclo de vida completo do Payment |
| Arquitetura | Clean Architecture 4 projetos | Fronteiras físicas explicitam dependências; cada camada tem um projeto |

---

## Regra de Dependências

```
PayFlow.API           → depende de → PayFlow.Application
PayFlow.Application   → depende de → PayFlow.Domain
PayFlow.Infrastructure → depende de → PayFlow.Domain
PayFlow.Infrastructure → depende de → PayFlow.Application
```

Domain não depende de ninguém. Infrastructure implementa interfaces definidas em Domain e Application.

---

## 1. Setup do Projeto

### 1.1 Criar a Solution e os Projetos

```bash
# Na raiz do repositório
dotnet new sln -n PayFlow

# Projetos da aplicação
dotnet new classlib -n PayFlow.Domain       -o src/PayFlow.Domain
dotnet new classlib -n PayFlow.Application  -o src/PayFlow.Application
dotnet new classlib -n PayFlow.Infrastructure -o src/PayFlow.Infrastructure
dotnet new webapi   -n PayFlow.API          -o src/PayFlow.API

# Projetos de teste
dotnet new xunit -n PayFlow.Domain.Tests          -o tests/PayFlow.Domain.Tests
dotnet new xunit -n PayFlow.Application.Tests     -o tests/PayFlow.Application.Tests
dotnet new xunit -n PayFlow.Infrastructure.Tests  -o tests/PayFlow.Infrastructure.Tests

# Adicionar todos à solution
dotnet sln add src/PayFlow.Domain/PayFlow.Domain.csproj
dotnet sln add src/PayFlow.Application/PayFlow.Application.csproj
dotnet sln add src/PayFlow.Infrastructure/PayFlow.Infrastructure.csproj
dotnet sln add src/PayFlow.API/PayFlow.API.csproj
dotnet sln add tests/PayFlow.Domain.Tests/PayFlow.Domain.Tests.csproj
dotnet sln add tests/PayFlow.Application.Tests/PayFlow.Application.Tests.csproj
dotnet sln add tests/PayFlow.Infrastructure.Tests/PayFlow.Infrastructure.Tests.csproj
```

### 1.2 Referências entre Projetos

```bash
# Application conhece Domain
dotnet add src/PayFlow.Application reference src/PayFlow.Domain

# Infrastructure implementa contratos de Domain e Application
dotnet add src/PayFlow.Infrastructure reference src/PayFlow.Domain
dotnet add src/PayFlow.Infrastructure reference src/PayFlow.Application

# API orquestra tudo (mas não referencia Infrastructure diretamente em código — apenas no DI)
dotnet add src/PayFlow.API reference src/PayFlow.Application
dotnet add src/PayFlow.API reference src/PayFlow.Infrastructure

# Testes
dotnet add tests/PayFlow.Domain.Tests reference src/PayFlow.Domain
dotnet add tests/PayFlow.Application.Tests reference src/PayFlow.Application
dotnet add tests/PayFlow.Application.Tests reference src/PayFlow.Domain
dotnet add tests/PayFlow.Infrastructure.Tests reference src/PayFlow.Infrastructure
dotnet add tests/PayFlow.Infrastructure.Tests reference src/PayFlow.Application
```

### 1.3 Pacotes NuGet por Projeto

**PayFlow.Domain** — sem dependências externas. Domain puro.
```bash
# Nenhum pacote. Domain não pode depender de bibliotecas externas.
```

**PayFlow.Application**
```bash
dotnet add src/PayFlow.Application package MediatR
dotnet add src/PayFlow.Application package FluentValidation
dotnet add src/PayFlow.Application package FluentValidation.DependencyInjectionExtensions
```

**PayFlow.Infrastructure**
```bash
dotnet add src/PayFlow.Infrastructure package Microsoft.EntityFrameworkCore
dotnet add src/PayFlow.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/PayFlow.Infrastructure package Microsoft.EntityFrameworkCore.Design
dotnet add src/PayFlow.Infrastructure package StackExchange.Redis
dotnet add src/PayFlow.Infrastructure package MediatR                     # para despachar commands no OutboxProcessor
dotnet add src/PayFlow.Infrastructure package Microsoft.Extensions.Hosting # BackgroundService
```

**PayFlow.API**
```bash
dotnet add src/PayFlow.API package MediatR.Extensions.Microsoft.DependencyInjection
dotnet add src/PayFlow.API package FluentValidation.AspNetCore
```

**Testes**
```bash
dotnet add tests/PayFlow.Domain.Tests package FluentAssertions
dotnet add tests/PayFlow.Application.Tests package FluentAssertions
dotnet add tests/PayFlow.Application.Tests package NSubstitute
dotnet add tests/PayFlow.Infrastructure.Tests package FluentAssertions
dotnet add tests/PayFlow.Infrastructure.Tests package NSubstitute
```

### 1.4 docker-compose.yml

Arquivo: `docker-compose.yml` (raiz do repositório).

```yaml
services:
  postgres:
    image: postgres:16
    environment:
      POSTGRES_DB: payflow
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5432:5432"

  redis:
    image: redis:7
    ports:
      - "6379:6379"
```

---

## 2. PayFlow.Domain

**Por que este projeto existe:** Contém as regras de negócio puras — o que um pagamento é, como ele muda de estado, quais eventos ocorrem. Não conhece banco de dados, HTTP, ou frameworks. É o núcleo estável que não muda quando a infraestrutura muda.

### 2.1 Exceção de Domínio

**Arquivo:** `src/PayFlow.Domain/Exceptions/DomainException.cs`

**Por que aqui:** Exceções que representam violações de regras de negócio pertencem ao Domain. A camada de apresentação as captura e mapeia para HTTP 422.

```csharp
namespace PayFlow.Domain.Exceptions;

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
```

---

### 2.2 Base de Domain Events

**Arquivo:** `src/PayFlow.Domain/Events/DomainEvent.cs`

**Por que aqui:** Todo event levantado pelo agregado herda desta classe. Pertence ao Domain porque events são parte da linguagem ubíqua do domínio.

```csharp
namespace PayFlow.Domain.Events;

public abstract class DomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
```

---

### 2.3 Value Objects

**Por que em `src/PayFlow.Domain/ValueObjects/`:** Value Objects encapsulam conceitos do domínio com igualdade por valor. Ficam em Domain porque representam tipos da linguagem ubíqua, não estruturas de dados genéricas.

#### `PaymentId.cs`
**Arquivo:** `src/PayFlow.Domain/ValueObjects/PaymentId.cs`

**Por que:** Evita confundir `Guid` de Payment com `Guid` de Customer. O compilador rejeita `new PaymentId(customerId)`.

```csharp
namespace PayFlow.Domain.ValueObjects;

public record PaymentId(Guid Value)
{
    public static PaymentId New() => new(Guid.NewGuid());

    public static PaymentId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("PaymentId cannot be empty.");
        return new(value);
    }
}
```

#### `CustomerId.cs`
**Arquivo:** `src/PayFlow.Domain/ValueObjects/CustomerId.cs`

```csharp
namespace PayFlow.Domain.ValueObjects;

public record CustomerId(Guid Value)
{
    public static CustomerId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("CustomerId cannot be empty.");
        return new(value);
    }
}
```

#### `MerchantId.cs`
**Arquivo:** `src/PayFlow.Domain/ValueObjects/MerchantId.cs`

```csharp
namespace PayFlow.Domain.ValueObjects;

public record MerchantId(Guid Value)
{
    public static MerchantId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("MerchantId cannot be empty.");
        return new(value);
    }
}
```

#### `Money.cs`
**Arquivo:** `src/PayFlow.Domain/ValueObjects/Money.cs`

**Por que:** Encapsula a regra de que Amount deve ser positivo e Currency não pode ser vazio. Evita `decimal` solto no agregado.

```csharp
namespace PayFlow.Domain.ValueObjects;

public record Money(decimal Amount, string Currency)
{
    public static Money Of(decimal amount, string currency)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency cannot be empty.");
        return new(amount, currency.ToUpperInvariant());
    }
}
```

---

### 2.4 PaymentStatus

**Arquivo:** `src/PayFlow.Domain/Aggregates/Payments/PaymentStatus.cs`

**Por que em `Aggregates/Payments/`:** O enum é parte integrante do agregado Payment — não tem significado fora dele. Fica na mesma pasta para deixar claro que pertence ao contexto do Payment.

```csharp
namespace PayFlow.Domain.Aggregates.Payments;

public enum PaymentStatus
{
    Pending,
    Processing,
    Approved,
    Failed,
    Cancelled
}
```

Transições válidas:
```
Pending    → Processing  (via StartProcessing)
Pending    → Cancelled   (via Cancel)
Processing → Approved    (via Approve)
Processing → Failed      (via Fail)
Processing → Cancelled   (via Cancel)
```

---

### 2.5 Domain Events do Payment

**Por que em `Aggregates/Payments/Events/`:** Events são levantados pelo agregado Payment e descrevem o que aconteceu nele. Ficam junto ao agregado para deixar claro de onde vêm e a qual contexto pertencem.

**Arquivo:** `src/PayFlow.Domain/Aggregates/Payments/Events/PaymentCreated.cs`
```csharp
namespace PayFlow.Domain.Aggregates.Payments.Events;

public sealed class PaymentCreated : DomainEvent
{
    public Guid PaymentId { get; init; }
    public Guid CustomerId { get; init; }
    public Guid MerchantId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = default!;
}
```

**Arquivo:** `src/PayFlow.Domain/Aggregates/Payments/Events/PaymentProcessingStarted.cs`
```csharp
public sealed class PaymentProcessingStarted : DomainEvent
{
    public Guid PaymentId { get; init; }
}
```

**Arquivo:** `src/PayFlow.Domain/Aggregates/Payments/Events/PaymentApproved.cs`
```csharp
public sealed class PaymentApproved : DomainEvent
{
    public Guid PaymentId { get; init; }
}
```

**Arquivo:** `src/PayFlow.Domain/Aggregates/Payments/Events/PaymentFailed.cs`
```csharp
public sealed class PaymentFailed : DomainEvent
{
    public Guid PaymentId { get; init; }
    public string Reason { get; init; } = default!;
}
```

**Arquivo:** `src/PayFlow.Domain/Aggregates/Payments/Events/PaymentCancelled.cs`
```csharp
public sealed class PaymentCancelled : DomainEvent
{
    public Guid PaymentId { get; init; }
}
```

---

### 2.6 Aggregate Root: Payment

**Arquivo:** `src/PayFlow.Domain/Aggregates/Payments/Payment.cs`

**Por que em `Aggregates/Payments/`:** O agregado é o objeto central do domínio. A pasta `Aggregates/` sinaliza que este é um Aggregate Root — objeto que controla consistência e levanta events. Tudo relacionado ao Payment (status, events) fica junto.

**Por que métodos e não setters:** Transições de estado passam por métodos que validam a transição antes de aplicá-la. Setters diretos permitiriam estados inválidos.

```csharp
namespace PayFlow.Domain.Aggregates.Payments;

public sealed class Payment
{
    private readonly List<DomainEvent> _domainEvents = new();

    public PaymentId Id { get; private set; } = default!;
    public CustomerId CustomerId { get; private set; } = default!;
    public MerchantId MerchantId { get; private set; } = default!;
    public Money Amount { get; private set; } = default!;
    public PaymentStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private Payment() { } // EF Core precisa do construtor sem parâmetros

    public static Payment Create(CustomerId customerId, MerchantId merchantId, Money amount)
    {
        var payment = new Payment
        {
            Id = PaymentId.New(),
            CustomerId = customerId,
            MerchantId = merchantId,
            Amount = amount,
            Status = PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        payment._domainEvents.Add(new PaymentCreated
        {
            PaymentId = payment.Id.Value,
            CustomerId = customerId.Value,
            MerchantId = merchantId.Value,
            Amount = amount.Amount,
            Currency = amount.Currency
        });

        return payment;
    }

    public void StartProcessing()
    {
        if (Status != PaymentStatus.Pending)
            throw new DomainException($"Cannot start processing a payment in status {Status}.");

        Status = PaymentStatus.Processing;
        UpdatedAt = DateTime.UtcNow;
        _domainEvents.Add(new PaymentProcessingStarted { PaymentId = Id.Value });
    }

    public void Approve()
    {
        if (Status != PaymentStatus.Processing)
            throw new DomainException($"Cannot approve a payment in status {Status}.");

        Status = PaymentStatus.Approved;
        UpdatedAt = DateTime.UtcNow;
        _domainEvents.Add(new PaymentApproved { PaymentId = Id.Value });
    }

    public void Fail(string reason)
    {
        if (Status != PaymentStatus.Processing)
            throw new DomainException($"Cannot fail a payment in status {Status}.");

        Status = PaymentStatus.Failed;
        UpdatedAt = DateTime.UtcNow;
        _domainEvents.Add(new PaymentFailed { PaymentId = Id.Value, Reason = reason });
    }

    public void Cancel()
    {
        if (Status != PaymentStatus.Pending && Status != PaymentStatus.Processing)
            throw new DomainException($"Cannot cancel a payment in status {Status}.");

        Status = PaymentStatus.Cancelled;
        UpdatedAt = DateTime.UtcNow;
        _domainEvents.Add(new PaymentCancelled { PaymentId = Id.Value });
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}
```

---

### 2.7 Interfaces de Domain (Ports)

**Por que em `src/PayFlow.Domain/Interfaces/`:** Estas interfaces são portas do domínio — o domínio declara o que precisa (persistir, commitar) sem saber como. Infrastructure as implementa. Ficam em Domain porque o próprio domínio (e Application) depende delas.

**Arquivo:** `src/PayFlow.Domain/Interfaces/IPaymentRepository.cs`

**Por que aqui e não em Application:** O repositório é uma abstração de persistência do agregado — pertence ao mesmo bounded context que o agregado.

```csharp
namespace PayFlow.Domain.Interfaces;

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(PaymentId id, CancellationToken ct = default);
    Task AddAsync(Payment payment, CancellationToken ct = default);
    Task UpdateAsync(Payment payment, CancellationToken ct = default);
}
```

**Arquivo:** `src/PayFlow.Domain/Interfaces/IUnitOfWork.cs`

**Por que aqui:** UnitOfWork garante que as operações do domínio são atômicas. É uma necessidade do domínio, não um detalhe de infraestrutura.

```csharp
namespace PayFlow.Domain.Interfaces;

public interface IUnitOfWork
{
    Task CommitAsync(CancellationToken ct = default);
}
```

---

## 3. PayFlow.Application

**Por que este projeto existe:** Orquestra os casos de uso. Recebe commands/queries, usa o Domain para aplicar regras de negócio, e coordena persistência e comunicação com serviços externos via interfaces. Não contém regras de negócio — contém fluxo de negócio.

### 3.1 Interfaces de Application (Ports de Orquestração)

**Por que em `src/PayFlow.Application/Interfaces/` e não em Domain:**
- `IPaymentGateway` — quem chama o gateway é o handler (Application), não o agregado. Domain não precisa conhecer gateways externos.
- `IPaymentReadRepository` — queries retornam DTOs, não agregados. É uma abstração de leitura que pertence à camada de orquestração.
- `IIdempotencyService` — controle de idempotência é uma preocupação de infraestrutura orquestrada pela Application.

**Arquivo:** `src/PayFlow.Application/Interfaces/IPaymentGateway.cs`
```csharp
namespace PayFlow.Application.Interfaces;

public interface IPaymentGateway
{
    Task<GatewayResponse> ProcessAsync(GatewayRequest request, CancellationToken ct = default);
}
```

**Arquivo:** `src/PayFlow.Application/Interfaces/IPaymentReadRepository.cs`
```csharp
namespace PayFlow.Application.Interfaces;

public interface IPaymentReadRepository
{
    Task<PaymentDetailDto?> GetDetailAsync(Guid paymentId, CancellationToken ct = default);
    Task<PagedResult<PaymentSummaryDto>> ListAsync(
        string? status, int page, int pageSize, CancellationToken ct = default);
}
```

**Arquivo:** `src/PayFlow.Application/Interfaces/IIdempotencyService.cs`
```csharp
namespace PayFlow.Application.Interfaces;

public interface IIdempotencyService
{
    Task<bool> HasBeenProcessedAsync(string key, CancellationToken ct = default);
    Task MarkAsProcessedAsync(string key, TimeSpan expiry, CancellationToken ct = default);
}
```

---

### 3.2 Contratos do Gateway

**Por que em `src/PayFlow.Application/Contracts/`:** São tipos de transferência de dados usados pela interface `IPaymentGateway`. Ficam em Application porque são definidos junto à interface que os usa. Infrastructure os recebe como parâmetro ao implementar a interface.

**Arquivo:** `src/PayFlow.Application/Contracts/GatewayRequest.cs`
```csharp
namespace PayFlow.Application.Contracts;

public record GatewayRequest(Guid PaymentId, decimal Amount, string Currency);
```

**Arquivo:** `src/PayFlow.Application/Contracts/GatewayResponse.cs`
```csharp
namespace PayFlow.Application.Contracts;

public record GatewayResponse(bool Success, string? TransactionId, string? ErrorMessage);
```

---

### 3.3 DTOs (Read Models)

**Por que em `src/PayFlow.Application/Payments/DTOs/`:** DTOs de leitura são contratos de saída dos QueryHandlers. Ficam em Application porque são produzidos pelos casos de uso de leitura e consumidos pela camada de apresentação (API).

**Arquivo:** `src/PayFlow.Application/Payments/DTOs/PaymentDetailDto.cs`
```csharp
namespace PayFlow.Application.Payments.DTOs;

public record PaymentDetailDto(
    Guid Id,
    Guid CustomerId,
    Guid MerchantId,
    decimal Amount,
    string Currency,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt
);
```

**Arquivo:** `src/PayFlow.Application/Payments/DTOs/PaymentSummaryDto.cs`
```csharp
namespace PayFlow.Application.Payments.DTOs;

public record PaymentSummaryDto(
    Guid Id,
    decimal Amount,
    string Currency,
    string Status,
    DateTime CreatedAt
);
```

**Arquivo:** `src/PayFlow.Application/Common/PagedResult.cs`

**Por que em `Common/`:** `PagedResult<T>` é genérico — não pertence ao contexto de Payments especificamente. `Common/` é para tipos compartilhados entre features.

```csharp
namespace PayFlow.Application.Common;

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize
);
```

---

### 3.4 Commands

**Por que em `src/PayFlow.Application/Payments/Commands/{NomeDoCommand}/`:** Cada command tem sua própria pasta com command, handler e validator. Agrupa o que muda junto. A pasta `Payments/` segmenta por feature — quando o projeto crescer com outras features, cada uma terá sua própria pasta.

#### CreatePayment

**Arquivo:** `src/PayFlow.Application/Payments/Commands/CreatePayment/CreatePaymentCommand.cs`

**Por que:** O Command é o input do caso de uso. É um record porque é imutável — representa uma intenção que não deve mudar durante o processamento.

```csharp
namespace PayFlow.Application.Payments.Commands.CreatePayment;

public record CreatePaymentCommand(
    Guid CustomerId,
    Guid MerchantId,
    decimal Amount,
    string Currency
) : IRequest<Guid>;
```

**Arquivo:** `src/PayFlow.Application/Payments/Commands/CreatePayment/CreatePaymentCommandValidator.cs`

**Por que aqui e não na API:** Validação de input é responsabilidade da Application — ela define o que é um command válido. A API apenas recebe o HTTP request e repassa. Assim, a validação funciona independente do transporte (HTTP, gRPC, fila).

```csharp
namespace PayFlow.Application.Payments.Commands.CreatePayment;

public class CreatePaymentCommandValidator : AbstractValidator<CreatePaymentCommand>
{
    public CreatePaymentCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.MerchantId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().MaximumLength(3);
    }
}
```

**Arquivo:** `src/PayFlow.Application/Payments/Commands/CreatePayment/CreatePaymentCommandHandler.cs`

**Por que:** O Handler orquestra o caso de uso. Ele NÃO contém regras de negócio — quem define que um Payment começa como Pending e levanta PaymentCreated é o agregado. O handler apenas conecta as peças.

```csharp
namespace PayFlow.Application.Payments.Commands.CreatePayment;

public class CreatePaymentCommandHandler : IRequestHandler<CreatePaymentCommand, Guid>
{
    private readonly IPaymentRepository _repository;
    private readonly IOutboxPublisher _outboxPublisher;
    private readonly IUnitOfWork _unitOfWork;

    public CreatePaymentCommandHandler(
        IPaymentRepository repository,
        IOutboxPublisher outboxPublisher,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _outboxPublisher = outboxPublisher;
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> Handle(CreatePaymentCommand command, CancellationToken ct)
    {
        var payment = Payment.Create(
            CustomerId.From(command.CustomerId),
            MerchantId.From(command.MerchantId),
            Money.Of(command.Amount, command.Currency)
        );

        await _repository.AddAsync(payment, ct);
        await _outboxPublisher.PublishAsync(payment.DomainEvents, ct);
        await _unitOfWork.CommitAsync(ct);  // persiste Payment + OutboxMessages na mesma transação

        return payment.Id.Value;
    }
}
```

---

#### CancelPayment

**Arquivo:** `src/PayFlow.Application/Payments/Commands/CancelPayment/CancelPaymentCommand.cs`
```csharp
namespace PayFlow.Application.Payments.Commands.CancelPayment;

public record CancelPaymentCommand(Guid PaymentId) : IRequest;
```

**Arquivo:** `src/PayFlow.Application/Payments/Commands/CancelPayment/CancelPaymentCommandValidator.cs`
```csharp
public class CancelPaymentCommandValidator : AbstractValidator<CancelPaymentCommand>
{
    public CancelPaymentCommandValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
    }
}
```

**Arquivo:** `src/PayFlow.Application/Payments/Commands/CancelPayment/CancelPaymentCommandHandler.cs`
```csharp
public class CancelPaymentCommandHandler : IRequestHandler<CancelPaymentCommand>
{
    private readonly IPaymentRepository _repository;
    private readonly IOutboxPublisher _outboxPublisher;
    private readonly IUnitOfWork _unitOfWork;

    public async Task Handle(CancelPaymentCommand command, CancellationToken ct)
    {
        var payment = await _repository.GetByIdAsync(PaymentId.From(command.PaymentId), ct)
            ?? throw new KeyNotFoundException($"Payment {command.PaymentId} not found.");

        payment.Cancel();  // lança DomainException se transição inválida

        await _outboxPublisher.PublishAsync(payment.DomainEvents, ct);
        await _unitOfWork.CommitAsync(ct);
    }
}
```

---

#### ProcessPayment (interno — chamado pelo OutboxProcessor)

**Por que este command existe:** O OutboxProcessor, ao ler um `PaymentCreated` do Outbox, precisa disparar o processamento. Em vez de chamar serviços diretamente, ele despacha um command via MediatR — mantendo a separação e permitindo testar o fluxo isoladamente.

**Arquivo:** `src/PayFlow.Application/Payments/Commands/ProcessPayment/ProcessPaymentCommand.cs`
```csharp
namespace PayFlow.Application.Payments.Commands.ProcessPayment;

public record ProcessPaymentCommand(Guid PaymentId) : IRequest;
```

**Arquivo:** `src/PayFlow.Application/Payments/Commands/ProcessPayment/ProcessPaymentCommandHandler.cs`
```csharp
public class ProcessPaymentCommandHandler : IRequestHandler<ProcessPaymentCommand>
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentGateway _gateway;
    private readonly IOutboxPublisher _outboxPublisher;
    private readonly IUnitOfWork _unitOfWork;

    public async Task Handle(ProcessPaymentCommand command, CancellationToken ct)
    {
        var payment = await _repository.GetByIdAsync(PaymentId.From(command.PaymentId), ct)
            ?? throw new KeyNotFoundException($"Payment {command.PaymentId} not found.");

        payment.StartProcessing();

        var request = new GatewayRequest(payment.Id.Value, payment.Amount.Amount, payment.Amount.Currency);
        var response = await _gateway.ProcessAsync(request, ct);

        if (response.Success)
            payment.Approve();
        else
            payment.Fail(response.ErrorMessage ?? "Gateway error");

        await _outboxPublisher.PublishAsync(payment.DomainEvents, ct);
        await _unitOfWork.CommitAsync(ct);
    }
}
```

---

### 3.5 Queries

**Por que em `src/PayFlow.Application/Payments/Queries/{NomeDaQuery}/`:** Queries ficam separadas de Commands porque têm características diferentes — não modificam estado, retornam DTOs e podem usar repositórios de leitura otimizados. MediatR trata ambos uniformemente.

**Arquivo:** `src/PayFlow.Application/Payments/Queries/GetPayment/GetPaymentQuery.cs`
```csharp
namespace PayFlow.Application.Payments.Queries.GetPayment;

public record GetPaymentQuery(Guid PaymentId) : IRequest<PaymentDetailDto?>;
```

**Arquivo:** `src/PayFlow.Application/Payments/Queries/GetPayment/GetPaymentQueryHandler.cs`
```csharp
public class GetPaymentQueryHandler : IRequestHandler<GetPaymentQuery, PaymentDetailDto?>
{
    private readonly IPaymentReadRepository _readRepository;

    public async Task<PaymentDetailDto?> Handle(GetPaymentQuery query, CancellationToken ct)
        => await _readRepository.GetDetailAsync(query.PaymentId, ct);
}
```

**Arquivo:** `src/PayFlow.Application/Payments/Queries/ListPayments/ListPaymentsQuery.cs`
```csharp
namespace PayFlow.Application.Payments.Queries.ListPayments;

public record ListPaymentsQuery(
    string? Status,
    int Page = 1,
    int PageSize = 20
) : IRequest<PagedResult<PaymentSummaryDto>>;
```

**Arquivo:** `src/PayFlow.Application/Payments/Queries/ListPayments/ListPaymentsQueryHandler.cs`
```csharp
public class ListPaymentsQueryHandler : IRequestHandler<ListPaymentsQuery, PagedResult<PaymentSummaryDto>>
{
    private readonly IPaymentReadRepository _readRepository;

    public async Task<PagedResult<PaymentSummaryDto>> Handle(ListPaymentsQuery query, CancellationToken ct)
        => await _readRepository.ListAsync(query.Status, query.Page, query.PageSize, ct);
}
```

---

### 3.6 Interface do OutboxPublisher

**Arquivo:** `src/PayFlow.Application/Interfaces/IOutboxPublisher.cs`

**Por que em Application e não em Domain:** O OutboxPublisher serializa domain events como OutboxMessages no banco. É uma preocupação de infraestrutura de entrega de mensagens — a Application define o contrato, Infrastructure implementa. Domain não sabe que existe um Outbox.

```csharp
namespace PayFlow.Application.Interfaces;

public interface IOutboxPublisher
{
    Task PublishAsync(IReadOnlyList<DomainEvent> events, CancellationToken ct = default);
}
```

---

### 3.7 DependencyInjection da Application

**Arquivo:** `src/PayFlow.Application/DependencyInjection.cs`

**Por que:** Centraliza o registro de todos os serviços da camada Application. A API chama este método e não precisa conhecer os detalhes internos.

```csharp
namespace PayFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        // Pipeline behavior para validação automática via FluentValidation
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        return services;
    }
}
```

**Arquivo:** `src/PayFlow.Application/Common/ValidationBehavior.cs`

**Por que:** Pipeline behavior do MediatR que intercepta todo command, executa os validators registrados e lança `ValidationException` antes de chegar no handler. Evita repetir `validator.ValidateAndThrowAsync()` em cada handler.

```csharp
namespace PayFlow.Application.Common;

public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
        => _validators = validators;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var context = new ValidationContext<TRequest>(request);
        var failures = _validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .ToList();

        if (failures.Any())
            throw new ValidationException(failures);

        return await next();
    }
}
```

---

## 4. PayFlow.Infrastructure

**Por que este projeto existe:** Implementa todas as interfaces declaradas em Domain e Application. Conhece PostgreSQL, Redis, EF Core, HTTP clients. É o único projeto que pode depender de bibliotecas externas de infraestrutura.

### 4.1 Persistência — DbContext

**Arquivo:** `src/PayFlow.Infrastructure/Persistence/PaymentDbContext.cs`

**Por que em `Persistence/`:** Agrupa tudo relacionado ao banco de dados. `PaymentDbContext` é o contexto do EF Core que mapeia o domínio para o banco.

```csharp
namespace PayFlow.Infrastructure.Persistence;

public class PaymentDbContext : DbContext
{
    public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options) { }

    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentDbContext).Assembly);
    }
}
```

---

### 4.2 Configurações EF Core

**Por que em `Persistence/Configurations/`:** Configurações do EF Core são detalhes de mapeamento ORM — não pertencem ao Domain (que é puro) nem ao DbContext (que ficaria grande). A pasta `Configurations/` segue o padrão de `IEntityTypeConfiguration<T>`.

**Arquivo:** `src/PayFlow.Infrastructure/Persistence/Configurations/PaymentConfiguration.cs`

**Por que:** Define como o agregado `Payment` e seus Value Objects são mapeados para colunas no PostgreSQL. EF Core não sabe por padrão como persistir `PaymentId` (record) ou `Money` (record com dois campos).

```csharp
namespace PayFlow.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");

        builder.HasKey(p => p.Id);

        // Value Object PaymentId → coluna "id" (Guid)
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => PaymentId.From(value));

        // Value Object CustomerId → coluna "customer_id"
        builder.Property(p => p.CustomerId)
            .HasColumnName("customer_id")
            .HasConversion(id => id.Value, value => CustomerId.From(value));

        // Value Object MerchantId → coluna "merchant_id"
        builder.Property(p => p.MerchantId)
            .HasColumnName("merchant_id")
            .HasConversion(id => id.Value, value => MerchantId.From(value));

        // Value Object Money → duas colunas inline
        builder.OwnsOne(p => p.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("amount");
            money.Property(m => m.Currency).HasColumnName("currency").HasMaxLength(3);
        });

        builder.Property(p => p.Status)
            .HasColumnName("status")
            .HasConversion<string>();  // persiste como "Pending", "Approved", etc.

        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        // Ignora a coleção de domain events — não é persistida
        builder.Ignore(p => p.DomainEvents);
    }
}
```

**Arquivo:** `src/PayFlow.Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs`
```csharp
public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.EventType).HasColumnName("event_type").HasMaxLength(200);
        builder.Property(o => o.Payload).HasColumnName("payload");
        builder.Property(o => o.CreatedAt).HasColumnName("created_at");
        builder.Property(o => o.ProcessedAt).HasColumnName("processed_at");
        builder.Property(o => o.Error).HasColumnName("error");
    }
}
```

---

### 4.3 Repositories

**Por que em `Persistence/Repositories/`:** Implementações concretas dos repositórios ficam em Infrastructure porque usam EF Core — um detalhe de infraestrutura. Os handlers só conhecem as interfaces.

**Arquivo:** `src/PayFlow.Infrastructure/Persistence/Repositories/PaymentRepository.cs`
```csharp
namespace PayFlow.Infrastructure.Persistence.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private readonly PaymentDbContext _context;

    public PaymentRepository(PaymentDbContext context) => _context = context;

    public async Task<Payment?> GetByIdAsync(PaymentId id, CancellationToken ct = default)
        => await _context.Payments.FindAsync(new object[] { id }, ct);

    public async Task AddAsync(Payment payment, CancellationToken ct = default)
        => await _context.Payments.AddAsync(payment, ct);

    public Task UpdateAsync(Payment payment, CancellationToken ct = default)
    {
        _context.Payments.Update(payment);
        return Task.CompletedTask;
    }
}
```

**Arquivo:** `src/PayFlow.Infrastructure/Persistence/Repositories/PaymentReadRepository.cs`

**Por que separado do PaymentRepository:** Leitura usa `AsNoTracking()` e retorna DTOs — nunca instancia o agregado completo. Manter separado deixa claro que é um repositório de leitura, sem risco de alguém chamar `Add` ou `Update` nele.

```csharp
public class PaymentReadRepository : IPaymentReadRepository
{
    private readonly PaymentDbContext _context;

    public PaymentReadRepository(PaymentDbContext context) => _context = context;

    public async Task<PaymentDetailDto?> GetDetailAsync(Guid paymentId, CancellationToken ct = default)
        => await _context.Payments
            .AsNoTracking()
            .Where(p => p.Id == PaymentId.From(paymentId))
            .Select(p => new PaymentDetailDto(
                p.Id.Value, p.CustomerId.Value, p.MerchantId.Value,
                p.Amount.Amount, p.Amount.Currency, p.Status.ToString(),
                p.CreatedAt, p.UpdatedAt))
            .FirstOrDefaultAsync(ct);

    public async Task<PagedResult<PaymentSummaryDto>> ListAsync(
        string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _context.Payments.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PaymentStatus>(status, out var parsedStatus))
            query = query.Where(p => p.Status == parsedStatus);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PaymentSummaryDto(
                p.Id.Value, p.Amount.Amount, p.Amount.Currency,
                p.Status.ToString(), p.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<PaymentSummaryDto>(items, total, page, pageSize);
    }
}
```

---

### 4.4 UnitOfWork

**Arquivo:** `src/PayFlow.Infrastructure/Persistence/UnitOfWork.cs`

**Por que:** Encapsula `SaveChangesAsync`. Os handlers chamam `IUnitOfWork.CommitAsync()` sem saber que existe EF Core por baixo.

```csharp
namespace PayFlow.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly PaymentDbContext _context;

    public UnitOfWork(PaymentDbContext context) => _context = context;

    public async Task CommitAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
```

---

### 4.5 Outbox — OutboxMessage

**Arquivo:** `src/PayFlow.Infrastructure/Outbox/OutboxMessage.cs`

**Por que em Infrastructure e não em Domain:** `OutboxMessage` é um artefato de infraestrutura — representa um registro no banco que rastreia a entrega de eventos. O Domain não sabe que existe Outbox; ele só levanta DomainEvents.

```csharp
namespace PayFlow.Infrastructure.Outbox;

public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventType { get; set; } = default!;   // ex: "PaymentCreated"
    public string Payload { get; set; } = default!;     // JSON do DomainEvent
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public string? Error { get; set; }
}
```

---

### 4.6 Outbox — OutboxPublisher

**Arquivo:** `src/PayFlow.Infrastructure/Outbox/OutboxPublisher.cs`

**Por que:** Converte DomainEvents em OutboxMessages e os adiciona ao DbContext na mesma transação que o agregado. Implementa `IOutboxPublisher` de Application.

```csharp
namespace PayFlow.Infrastructure.Outbox;

public class OutboxPublisher : IOutboxPublisher
{
    private readonly PaymentDbContext _context;

    public OutboxPublisher(PaymentDbContext context) => _context = context;

    public async Task PublishAsync(IReadOnlyList<DomainEvent> events, CancellationToken ct = default)
    {
        foreach (var domainEvent in events)
        {
            var message = new OutboxMessage
            {
                EventType = domainEvent.GetType().Name,
                Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType())
            };
            await _context.OutboxMessages.AddAsync(message, ct);
        }
    }
}
```

---

### 4.7 Outbox — OutboxProcessor (Cron Worker)

**Arquivo:** `src/PayFlow.Infrastructure/Outbox/OutboxProcessor.cs`

**Por que BackgroundService:** Precisa rodar em background continuamente enquanto a aplicação está rodando. `BackgroundService` do .NET é a abstração padrão para isso.

**Por que em Infrastructure:** O OutboxProcessor lê do banco e despacha via MediatR — usa EF Core (infraestrutura) e MediatR (orquestração). Não pertence ao Domain nem à Application pura.

```csharp
namespace PayFlow.Infrastructure.Outbox;

public class OutboxProcessor : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly OutboxOptions _options;

    public OutboxProcessor(IServiceProvider serviceProvider, ILogger<OutboxProcessor> logger, IOptions<OutboxOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessBatchAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(_options.IntervalSeconds), stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var idempotency = scope.ServiceProvider.GetRequiredService<IIdempotencyService>();

        var messages = await context.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        foreach (var message in messages)
        {
            var idempotencyKey = $"outbox:{message.EventType}:{message.Id}";

            try
            {
                if (await idempotency.HasBeenProcessedAsync(idempotencyKey, ct))
                {
                    message.ProcessedAt = DateTime.UtcNow;
                    continue;
                }

                await DispatchAsync(message, mediator, ct);

                await idempotency.MarkAsProcessedAsync(idempotencyKey, TimeSpan.FromHours(24), ct);
                message.ProcessedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process outbox message {MessageId}", message.Id);
                message.Error = ex.Message;
            }
        }

        await context.SaveChangesAsync(ct);
    }

    private static async Task DispatchAsync(OutboxMessage message, IMediator mediator, CancellationToken ct)
    {
        // Mapeia EventType → Command a despachar
        if (message.EventType == nameof(PaymentCreated))
        {
            var @event = JsonSerializer.Deserialize<PaymentCreated>(message.Payload)!;
            await mediator.Send(new ProcessPaymentCommand(@event.PaymentId), ct);
        }
        // Outros eventos podem ser mapeados aqui conforme o sistema cresce
    }
}
```

**Arquivo:** `src/PayFlow.Infrastructure/Outbox/OutboxOptions.cs`
```csharp
namespace PayFlow.Infrastructure.Outbox;

public class OutboxOptions
{
    public int IntervalSeconds { get; set; } = 10;
    public int BatchSize { get; set; } = 50;
}
```

---

### 4.8 Gateway — FakePaymentGateway

**Arquivo:** `src/PayFlow.Infrastructure/Gateway/FakePaymentGateway.cs`

**Por que em Infrastructure:** É uma implementação concreta de `IPaymentGateway`. Simula o comportamento de um gateway externo. Quando houver integração real com Stripe, basta criar `StripePaymentGateway` na mesma pasta e trocar o registro no DI.

```csharp
namespace PayFlow.Infrastructure.Gateway;

public class FakePaymentGateway : IPaymentGateway
{
    private readonly FakeGatewayOptions _options;

    public FakePaymentGateway(IOptions<FakeGatewayOptions> options) => _options = options.Value;

    public async Task<GatewayResponse> ProcessAsync(GatewayRequest request, CancellationToken ct = default)
    {
        await Task.Delay(_options.DelayMs, ct);  // simula latência de rede

        if (request.Amount >= _options.FailureThreshold)
            return new GatewayResponse(false, null, "Insufficient funds");

        return new GatewayResponse(true, Guid.NewGuid().ToString(), null);
    }
}
```

**Arquivo:** `src/PayFlow.Infrastructure/Gateway/FakeGatewayOptions.cs`
```csharp
namespace PayFlow.Infrastructure.Gateway;

public class FakeGatewayOptions
{
    public decimal FailureThreshold { get; set; } = 10000;
    public int DelayMs { get; set; } = 500;
}
```

---

### 4.9 Idempotência — RedisIdempotencyService

**Arquivo:** `src/PayFlow.Infrastructure/Idempotency/RedisIdempotencyService.cs`

**Por que em Infrastructure:** Usa Redis (detalhe de infraestrutura). A interface `IIdempotencyService` é de Application; a implementação com Redis fica em Infrastructure.

```csharp
namespace PayFlow.Infrastructure.Idempotency;

public class RedisIdempotencyService : IIdempotencyService
{
    private readonly IConnectionMultiplexer _redis;

    public RedisIdempotencyService(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<bool> HasBeenProcessedAsync(string key, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        return await db.KeyExistsAsync(key);
    }

    public async Task MarkAsProcessedAsync(string key, TimeSpan expiry, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync(key, "1", expiry, When.NotExists);
    }
}
```

---

### 4.10 DependencyInjection da Infrastructure

**Arquivo:** `src/PayFlow.Infrastructure/DependencyInjection.cs`

**Por que:** Centraliza o registro de todas as implementações de Infrastructure. A API chama este método. Nenhuma classe de Infrastructure é referenciada diretamente fora deste arquivo.

```csharp
namespace PayFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // EF Core + PostgreSQL
        services.AddDbContext<PaymentDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres")));

        // Redis
        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis")!));

        // Repositórios
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentReadRepository, PaymentReadRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Outbox
        services.AddScoped<IOutboxPublisher, OutboxPublisher>();
        services.Configure<OutboxOptions>(configuration.GetSection("Outbox"));
        services.AddHostedService<OutboxProcessor>();

        // Gateway
        services.Configure<FakeGatewayOptions>(configuration.GetSection("FakeGateway"));
        services.AddScoped<IPaymentGateway, FakePaymentGateway>();

        // Idempotência
        services.AddScoped<IIdempotencyService, RedisIdempotencyService>();

        return services;
    }
}
```

---

### 4.11 Migrations

```bash
# Criar a primeira migration (rodar na raiz da solution)
dotnet ef migrations add InitialCreate \
    --project src/PayFlow.Infrastructure \
    --startup-project src/PayFlow.API

# Aplicar ao banco
dotnet ef database update \
    --project src/PayFlow.Infrastructure \
    --startup-project src/PayFlow.API
```

**Por que `--project` aponta para Infrastructure:** As migrations pertencem à Infrastructure — é onde `PaymentDbContext` vive.
**Por que `--startup-project` aponta para API:** O EF Core precisa de um projeto executável com a connection string configurada para rodar as migrations.

As migrations são geradas em: `src/PayFlow.Infrastructure/Persistence/Migrations/`

---

## 5. PayFlow.API

**Por que este projeto existe:** Recebe requisições HTTP, traduz para Commands/Queries, despacha via MediatR, e devolve respostas. Não contém lógica de negócio nem de infraestrutura.

### 5.1 Controller

**Arquivo:** `src/PayFlow.API/Controllers/PaymentsController.cs`

**Por que um único controller:** Todo o domínio é de Payments. Quando o sistema crescer com outros contextos, cada um terá seu controller.

```csharp
namespace PayFlow.API.Controllers;

[ApiController]
[Route("payments")]
public class PaymentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PaymentsController(IMediator mediator) => _mediator = mediator;

    // POST /payments
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePaymentCommand command, CancellationToken ct)
    {
        var paymentId = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = paymentId }, new { paymentId });
    }

    // GET /payments/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPaymentQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
    }

    // GET /payments?status=Pending&page=1&pageSize=20
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] ListPaymentsQuery query, CancellationToken ct)
        => Ok(await _mediator.Send(query, ct));

    // POST /payments/{id}/cancel
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new CancelPaymentCommand(id), ct);
        return NoContent();
    }

    // POST /payments/webhook — simula callback do gateway externo
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook([FromBody] WebhookPayload payload, CancellationToken ct)
    {
        // Chama diretamente o repositório via mediator — aqui usamos um command específico
        // Ver WebhookCommand abaixo
        await _mediator.Send(new WebhookCommand(payload.PaymentId, payload.Success, payload.ErrorMessage), ct);
        return Ok();
    }
}
```

**Arquivo:** `src/PayFlow.API/Controllers/WebhookPayload.cs`
```csharp
namespace PayFlow.API.Controllers;

public record WebhookPayload(Guid PaymentId, bool Success, string? TransactionId, string? ErrorMessage);
```

**Arquivo:** `src/PayFlow.Application/Payments/Commands/Webhook/WebhookCommand.cs`
```csharp
namespace PayFlow.Application.Payments.Commands.Webhook;

public record WebhookCommand(Guid PaymentId, bool Success, string? ErrorMessage) : IRequest;
```

**Arquivo:** `src/PayFlow.Application/Payments/Commands/Webhook/WebhookCommandHandler.cs`
```csharp
public class WebhookCommandHandler : IRequestHandler<WebhookCommand>
{
    private readonly IPaymentRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public async Task Handle(WebhookCommand command, CancellationToken ct)
    {
        var payment = await _repository.GetByIdAsync(PaymentId.From(command.PaymentId), ct)
            ?? throw new KeyNotFoundException($"Payment {command.PaymentId} not found.");

        // Se ainda está Pending, precisa passar por Processing antes
        if (payment.Status == PaymentStatus.Pending)
            payment.StartProcessing();

        if (command.Success)
            payment.Approve();
        else
            payment.Fail(command.ErrorMessage ?? "Gateway callback failure");

        await _unitOfWork.CommitAsync(ct);
    }
}
```

---

### 5.2 Middleware de Exceções

**Arquivo:** `src/PayFlow.API/Middleware/GlobalExceptionHandler.cs`

**Por que:** Mapeia exceções de domínio e aplicação para respostas HTTP sem poluir os controllers com try/catch.

```csharp
namespace PayFlow.API.Middleware;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            DomainException       => (422, "Domain Rule Violation"),
            ValidationException   => (400, "Validation Error"),
            KeyNotFoundException  => (404, "Not Found"),
            _                    => (500, "Internal Server Error")
        };

        if (status == 500)
            _logger.LogError(ex, "Unhandled exception");

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new { title, detail = ex.Message }, ct);
        return true;
    }
}
```

---

### 5.3 Program.cs

**Arquivo:** `src/PayFlow.API/Program.cs`

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.MapControllers();
app.Run();
```

---

### 5.4 appsettings.json

**Arquivo:** `src/PayFlow.API/appsettings.json`

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=payflow;Username=postgres;Password=postgres",
    "Redis": "localhost:6379"
  },
  "Outbox": {
    "IntervalSeconds": 10,
    "BatchSize": 50
  },
  "FakeGateway": {
    "FailureThreshold": 10000,
    "DelayMs": 500
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

---

## 6. Fluxos Principais

### Fluxo 1 — Criar Pagamento (escrita + async)

```
POST /payments
  │
  ▼
PaymentsController.Create
  │  recebe { customerId, merchantId, amount, currency }
  ▼
MediatR.Send(CreatePaymentCommand)
  │
  ├─► ValidationBehavior  →  FluentValidation valida o command
  │
  ▼
CreatePaymentCommandHandler
  ├─► Payment.Create(...)               [Domain] levanta PaymentCreated
  ├─► IPaymentRepository.AddAsync(...)  [rastreia no DbContext]
  ├─► IOutboxPublisher.PublishAsync(...) [adiciona OutboxMessage ao DbContext]
  └─► IUnitOfWork.CommitAsync()         [SaveChangesAsync — transação única]
  │
  ▼
201 Created { paymentId }

(em background, a cada 10s)
OutboxProcessor.ProcessBatchAsync
  ├─► lê OutboxMessages onde ProcessedAt IS NULL
  ├─► IIdempotencyService.HasBeenProcessedAsync("outbox:PaymentCreated:{id}") → false
  ├─► MediatR.Send(ProcessPaymentCommand { PaymentId })
  │     ▼
  │   ProcessPaymentCommandHandler
  │     ├─► payment.StartProcessing()          [Pending → Processing]
  │     ├─► IPaymentGateway.ProcessAsync(...)  [FakePaymentGateway]
  │     ├─► payment.Approve() ou Fail(reason)  [Processing → Approved|Failed]
  │     └─► IUnitOfWork.CommitAsync()
  ├─► IIdempotencyService.MarkAsProcessedAsync(key, 24h)
  └─► OutboxMessage.ProcessedAt = now → SaveChangesAsync
```

### Fluxo 2 — Cancelar Pagamento

```
POST /payments/{id}/cancel
  │
  ▼
CancelPaymentCommandHandler
  ├─► IPaymentRepository.GetByIdAsync(...)
  ├─► payment.Cancel()           [lança DomainException se status inválido]
  ├─► IOutboxPublisher.PublishAsync(PaymentCancelled)
  └─► IUnitOfWork.CommitAsync()
  │
  ▼
204 No Content
(ou 422 se DomainException → GlobalExceptionHandler)
```

### Fluxo 3 — Webhook Simulado

```
POST /payments/webhook
  Body: { paymentId, success, errorMessage? }
  │
  ▼
WebhookCommandHandler
  ├─► IPaymentRepository.GetByIdAsync(...)
  ├─► payment.StartProcessing() [se ainda Pending]
  ├─► payment.Approve() ou Fail(reason)
  └─► IUnitOfWork.CommitAsync()
  │
  ▼
200 OK
```

---

## 7. Estrutura Final de Pastas

```
pay-flow/
├── src/
│   ├── PayFlow.Domain/
│   │   ├── Aggregates/
│   │   │   └── Payments/
│   │   │       ├── Payment.cs
│   │   │       ├── PaymentStatus.cs
│   │   │       └── Events/
│   │   │           ├── PaymentCreated.cs
│   │   │           ├── PaymentProcessingStarted.cs
│   │   │           ├── PaymentApproved.cs
│   │   │           ├── PaymentFailed.cs
│   │   │           └── PaymentCancelled.cs
│   │   ├── ValueObjects/
│   │   │   ├── PaymentId.cs
│   │   │   ├── CustomerId.cs
│   │   │   ├── MerchantId.cs
│   │   │   └── Money.cs
│   │   ├── Events/
│   │   │   └── DomainEvent.cs
│   │   ├── Exceptions/
│   │   │   └── DomainException.cs
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
│   │   │   │   │   ├── CreatePaymentCommand.cs
│   │   │   │   │   ├── CreatePaymentCommandHandler.cs
│   │   │   │   │   └── CreatePaymentCommandValidator.cs
│   │   │   │   ├── CancelPayment/
│   │   │   │   │   ├── CancelPaymentCommand.cs
│   │   │   │   │   ├── CancelPaymentCommandHandler.cs
│   │   │   │   │   └── CancelPaymentCommandValidator.cs
│   │   │   │   ├── ProcessPayment/
│   │   │   │   │   ├── ProcessPaymentCommand.cs
│   │   │   │   │   └── ProcessPaymentCommandHandler.cs
│   │   │   │   └── Webhook/
│   │   │   │       ├── WebhookCommand.cs
│   │   │   │       └── WebhookCommandHandler.cs
│   │   │   ├── Queries/
│   │   │   │   ├── GetPayment/
│   │   │   │   │   ├── GetPaymentQuery.cs
│   │   │   │   │   └── GetPaymentQueryHandler.cs
│   │   │   │   └── ListPayments/
│   │   │   │       ├── ListPaymentsQuery.cs
│   │   │   │       └── ListPaymentsQueryHandler.cs
│   │   │   └── DTOs/
│   │   │       ├── PaymentDetailDto.cs
│   │   │       └── PaymentSummaryDto.cs
│   │   └── DependencyInjection.cs
│   │
│   ├── PayFlow.Infrastructure/
│   │   ├── Gateway/
│   │   │   ├── FakePaymentGateway.cs
│   │   │   └── FakeGatewayOptions.cs
│   │   ├── Idempotency/
│   │   │   └── RedisIdempotencyService.cs
│   │   ├── Outbox/
│   │   │   ├── OutboxMessage.cs
│   │   │   ├── OutboxOptions.cs
│   │   │   ├── OutboxPublisher.cs
│   │   │   └── OutboxProcessor.cs
│   │   ├── Persistence/
│   │   │   ├── Configurations/
│   │   │   │   ├── PaymentConfiguration.cs
│   │   │   │   └── OutboxMessageConfiguration.cs
│   │   │   ├── Migrations/          ← gerado pelo EF Core
│   │   │   ├── Repositories/
│   │   │   │   ├── PaymentRepository.cs
│   │   │   │   └── PaymentReadRepository.cs
│   │   │   ├── PaymentDbContext.cs
│   │   │   └── UnitOfWork.cs
│   │   └── DependencyInjection.cs
│   │
│   └── PayFlow.API/
│       ├── Controllers/
│       │   ├── PaymentsController.cs
│       │   └── WebhookPayload.cs
│       ├── Middleware/
│       │   └── GlobalExceptionHandler.cs
│       ├── Program.cs
│       └── appsettings.json
│
├── tests/
│   ├── PayFlow.Domain.Tests/
│   │   └── Payments/
│   │       └── PaymentTests.cs
│   ├── PayFlow.Application.Tests/
│   │   └── Payments/
│   │       ├── CreatePaymentTests.cs
│   │       └── CancelPaymentTests.cs
│   └── PayFlow.Infrastructure.Tests/
│       └── Outbox/
│           └── OutboxProcessorTests.cs
│
├── docs/
│   └── superpowers/
│       └── specs/
│           └── 2026-06-12-payment-api-design.md
├── docker-compose.yml
└── PayFlow.sln
```

---

## 8. Sequência de Implementação

Implementar nesta ordem garante que cada camada pode ser compilada e testada antes de depender da próxima.

```
Passo 1 — PayFlow.Domain
  ├── DomainEvent.cs
  ├── DomainException.cs
  ├── Value Objects (PaymentId, CustomerId, MerchantId, Money)
  ├── PaymentStatus.cs
  ├── Events (PaymentCreated, PaymentProcessingStarted, PaymentApproved, PaymentFailed, PaymentCancelled)
  ├── Payment.cs (aggregate)
  ├── IPaymentRepository.cs
  └── IUnitOfWork.cs

Passo 2 — PayFlow.Application (contratos e interfaces)
  ├── GatewayRequest.cs, GatewayResponse.cs
  ├── IPaymentGateway.cs, IPaymentReadRepository.cs, IOutboxPublisher.cs, IIdempotencyService.cs
  ├── PagedResult.cs
  └── DTOs (PaymentDetailDto, PaymentSummaryDto)

Passo 3 — PayFlow.Application (commands e queries)
  ├── CreatePaymentCommand + Handler + Validator
  ├── CancelPaymentCommand + Handler + Validator
  ├── ProcessPaymentCommand + Handler
  ├── WebhookCommand + Handler
  ├── GetPaymentQuery + Handler
  ├── ListPaymentsQuery + Handler
  ├── ValidationBehavior.cs
  └── DependencyInjection.cs

Passo 4 — PayFlow.Domain.Tests
  └── PaymentTests.cs — testa todas as transições de estado e domain events

Passo 5 — PayFlow.Infrastructure (persistência)
  ├── PaymentDbContext.cs
  ├── PaymentConfiguration.cs, OutboxMessageConfiguration.cs
  ├── PaymentRepository.cs, UnitOfWork.cs
  ├── Migration inicial (dotnet ef migrations add)
  └── docker-compose up (postgres + redis)

Passo 6 — PayFlow.Infrastructure (outbox)
  ├── OutboxMessage.cs
  ├── OutboxPublisher.cs
  └── OutboxProcessor.cs + OutboxOptions.cs

Passo 7 — PayFlow.Infrastructure (gateway + idempotência)
  ├── FakePaymentGateway.cs + FakeGatewayOptions.cs
  ├── RedisIdempotencyService.cs
  ├── PaymentReadRepository.cs
  └── DependencyInjection.cs

Passo 8 — PayFlow.API
  ├── PaymentsController.cs + WebhookPayload.cs
  ├── GlobalExceptionHandler.cs
  ├── Program.cs
  └── appsettings.json

Passo 9 — Testes restantes
  ├── PayFlow.Application.Tests (handlers com mocks de IPaymentRepository, IUnitOfWork)
  └── PayFlow.Infrastructure.Tests (OutboxProcessor com idempotência)
```
