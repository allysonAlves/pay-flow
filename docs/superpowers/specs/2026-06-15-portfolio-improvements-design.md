# PayFlow — Portfolio Improvements Design

## Context

PayFlow is a payment processing API built with Clean Architecture, DDD, CQRS, and the Outbox Pattern. Before adding to portfolio, five gaps were identified: missing authentication, no webhook signature validation, API contract coupled to application command, inefficient EF Core update, and no health checks or README.

---

## 1. Security

### 1.1 API Key Authentication

**Scope:** All endpoints except `POST /payments/webhook`.

**Mechanism:**
- Client sends `X-Api-Key: <key>` header on every request.
- A new `ApiKeyMiddleware` in `PayFlow.API/Middleware/` reads the header and compares it (constant-time comparison via `CryptographicOperations.FixedTimeEquals`) against the configured value.
- If the header is missing or the value does not match, the middleware short-circuits with HTTP `401` and a `ProblemDetails` body consistent with the existing `GlobalExceptionHandler` format.
- If valid, the middleware calls `next(context)`.

**Configuration — additions to `appsettings.json`:**
```json
{
  "Auth": {
    "ApiKey": "dev-key-change-in-production"
  },
  "Webhook": {
    "Secret": "dev-secret-change-in-production"
  }
}
```
In production, `Auth__ApiKey` and `Webhook__Secret` are supplied via environment variables or secret manager (Azure Key Vault, AWS Secrets Manager). The development values are placeholders only.

**Registration (`Program.cs`):**
```csharp
app.UseMiddleware<ApiKeyMiddleware>(); // before MapControllers()
```

**Exclusion:** The middleware checks `context.Request.Path.StartsWithSegments("/payments/webhook")` and calls `next()` directly — the webhook has its own auth.

---

### 1.2 Webhook HMAC Signature Validation

**Scope:** `POST /payments/webhook` only.

**Mechanism:**
- The payment gateway includes a `X-Webhook-Signature` header containing `HMAC-SHA256(secret, rawBody)` encoded as hex.
- A `WebhookSignatureFilter` (implementing `IActionFilter`) runs before the action:
  1. Calls `context.HttpContext.Request.EnableBuffering()` to allow the body to be read more than once.
  2. Reads the raw request body as a string.
  3. Rewinds `Request.Body` to position 0 so the model binder can read it normally afterward.
  4. Computes `HMAC-SHA256(Webhook:Secret, rawBody)`.
  5. Compares result with the header value using `CryptographicOperations.FixedTimeEquals`.
  6. Returns HTTP `401` with `ProblemDetails` if invalid or header is missing.
- Applied to the webhook action via `[ServiceFilter(typeof(WebhookSignatureFilter))]`.
- `WebhookSignatureFilter` must be registered in DI as `services.AddScoped<WebhookSignatureFilter>()` in `PayFlow.API` or `PayFlow.Infrastructure` DI extension.

**Configuration (`appsettings.json`):**
```json
{
  "Webhook": {
    "Secret": "<value-overridden-by-env-var>"
  }
}
```

**Files:**
- `PayFlow.API/Middleware/ApiKeyMiddleware.cs`
- `PayFlow.API/Filters/WebhookSignatureFilter.cs`

---

## 2. Architecture Fixes

### 2.1 DTO Separation — CreatePayment

**Problem:** `PaymentsController` receives `CreatePaymentCommand` directly from `[FromBody]`, coupling the HTTP contract to the Application layer. A rename or restructure in the command breaks the API contract.

**Fix:** Introduce `CreatePaymentRequest` in `PayFlow.API/Requests/`:

```csharp
public record CreatePaymentRequest(Guid CustomerId, Guid MerchantId, decimal Amount, string Currency);
```

Controller maps manually to the command:
```csharp
public async Task<IActionResult> Create([FromBody] CreatePaymentRequest request, CancellationToken ct)
{
    var command = new CreatePaymentCommand(request.CustomerId, request.MerchantId, request.Amount, request.Currency);
    var paymentId = await _mediator.Send(command, ct);
    return CreatedAtAction(nameof(GetById), new { id = paymentId }, new { paymentId });
}
```

No AutoMapper — four-field manual mapping does not justify the dependency.

---

### 2.2 EF Core UpdateAsync Fix

**Problem:** `PaymentRepository.UpdateAsync` calls `_context.Payments.Update(payment)`, which marks all properties as `Modified` and generates a full-column `UPDATE` statement regardless of what changed.

**Fix:** Remove the explicit call. EF Core's change tracker already tracks the entity when it is fetched via `FindAsync` within the same `DbContext` scope. `UnitOfWork.CommitAsync()` calls `SaveChangesAsync()` which flushes only the actual changes.

```csharp
public Task UpdateAsync(Payment payment, CancellationToken ct = default)
    => Task.CompletedTask;
```

---

## 3. Operational

### 3.1 Health Checks

**Endpoint:** `GET /health`

**Checks:**
| Check | Package | What it verifies |
|---|---|---|
| EF Core | `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` | Can open a connection to Postgres |
| Redis | `AspNetCore.HealthChecks.Redis` | Can ping the Redis instance |

**Registration in `PayFlow.Infrastructure/DependencyInjection.cs`:**
```csharp
services.AddHealthChecks()
    .AddDbContextCheck<PaymentDbContext>()
    .AddRedis(configuration.GetConnectionString("Redis")!);
```

**Registration in `Program.cs`:**
```csharp
app.MapHealthChecks("/health");
```

Response format: standard ASP.NET health check JSON (`status`, `results` per check).

---

### 3.2 README

**Location:** `README.md` at repository root.

**Sections:**
1. **What this is** — one paragraph describing PayFlow as a portfolio project demonstrating DDD, Clean Architecture, CQRS, Outbox Pattern, and idempotency.
2. **Architecture overview** — ASCII flow diagram:
   ```
   POST /payments
     └── CreatePaymentCommand
           └── Payment.Create() → outbox[PaymentCreated]
                 └── OutboxProcessor → PaymentCreatedIntegrationEvent
                       └── ProcessPaymentCommand
                             └── Gateway → Approve() / Fail()
                                   └── outbox[PaymentApproved | PaymentFailed]
                                         └── NotifyCustomer / NotifyMerchant
   ```
3. **Key patterns** — brief bullet per pattern with the file that best illustrates it.
4. **Running locally** — prerequisites (Docker), commands (`docker-compose up`, `dotnet run`).
5. **Environment variables** — table: variable name, description, example value.
6. **Fake implementations** — note that `FakePaymentGateway` and `FakeCustomerNotifier`/`FakeMerchantNotifier` are stubs; in production these would be HTTP clients to real providers.

---

## Out of Scope

- Multiple API keys / key rotation (would require a keys table; overkill for portfolio)
- JWT authentication (API Key is the correct model for B2B payment APIs)
- Swagger auth UI (`Authorize` button) — nice-to-have, not blocking
- Integration tests against real DB/Redis — existing unit tests are sufficient for portfolio
