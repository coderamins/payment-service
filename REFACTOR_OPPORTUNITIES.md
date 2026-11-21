# Payment Service - Refactor Opportunities

## Priority 1: Critical (Architecture/Duplication)

### 1. Duplicated `GetParameter` Helper Across All Gateways
**Files:** 
- `MellatGateway.cs:79` (inline in `ParseCallback`)
- `PasargadGateway.cs:152-158`
- `ParsianGateway.cs:115-121`
- `SamanGateway.cs:141-147`
- `IranKishGateway.cs:79-84`
- `AsanPardakhtGateway.cs:206-215`
- `MelliGateway.cs:147-148`
- `PardakhtNovinGateway.cs:112-113`

**Issue:** Identical case-insensitive dictionary lookup logic duplicated in 8 gateway implementations.

**Fix:** Extract to `GatewayParameterHelper.GetParameter(IDictionary<string,string>, string)` in `PaymentService.Application.Abstractions` or shared infrastructure.

---

### 2. Duplicated Gateway Error Mapping Patterns
**Files:** All gateway implementations have private `MapError`, `MapVerifyError`, `MapSettlementError`, `MapReverseError` methods with similar switch expressions.

**Examples:**
- `MellatGateway.cs:159-173` (MapError)
- `PasargadGateway.cs:172-214` (MapError + MapCallbackStatus)
- `ParsianGateway.cs:123-142` (MapError)
- `SamanGateway.cs:149-174` (MapCallbackError + MapResultCodeMessage)
- `IranKishGateway.cs`: inline error strings
- `AsanPardakhtGateway.cs:149-180` (MapTokenError, MapVerifyError, MapSettlementError)
- `MelliGateway.cs:150-186` (MapError, MapVerifyError, MapReverseError)
- `PardakhtNovinGateway.cs:115-132` (MapError)

**Fix:** Create `IGatewayErrorMapper` abstraction with default implementation; gateways provide error code → message dictionaries or resource files.

---

### 3. Large `PaymentOrchestrationService` (478 lines, 14 methods)
**File:** `PaymentOrchestrationService.cs`

**Violations:**
- Handles payment creation, callback confirmation, settlement, reversal, status queries
- Mixes orchestration logic with domain logic (amount mismatch handling, concurrency control)
- Direct dependency on `IGatewayHealthMonitor`, `IOutboxWriter`, `IPaymentTransactionRepository`

**Fix:** Split into:
- `PaymentCreationService` (CreatePaymentAsync)
- `PaymentCallbackService` (ConfirmCallbackAsync + HandleAmountMismatchAsync)
- `PaymentSettlementService` (SettlePaymentAsync)
- `PaymentReversalService` (ReverseAsync)
- `PaymentQueryService` (GetStatusAsync, GetStatusByOrderIdAsync, GetReturnUrlAsync)

---

## Priority 2: High (Maintainability/Reliability)

### 4. Magic Strings for Gateway Provider in Callback URL
**File:** `PaymentOrchestrationService.cs:418`
```csharp
var provider = transaction.GatewayProviderId.ToString().ToLowerInvariant();
return $"{baseUrl}/api/v1/payments/callback/{provider}/{transaction.CallbackKey}";
```

**Issue:** Couples domain enum to URL routing; breaks if enum name changes or routing convention differs (e.g., Pasargad uses GET callback at different route).

**Fix:** Add `CallbackRouteSegment` property to `GatewayProvider` enum extension or configuration; use `IPaymentGateway.GetCallbackRouteSegment()`.

---

### 5. Hardcoded "NO_SALE_REF", "NO_TOKEN", "NO_RESPONSE" Error Codes
**Files:** 
- `MellatGateway.cs:112, 129, 146`
- `PasargadGateway.cs:85, 116, 135`
- `ParsianGateway.cs:88, 105`
- `SamanGateway.cs:103, 129`
- `AsanPardakhtGateway.cs:106, 126`
- `MelliGateway.cs:85, 116`
- `PardakhtNovinGateway.cs:81, 100`

**Fix:** Define constants in `GatewayOperationResult` or shared `GatewayErrorCodes` class.

---

### 6. Inconsistent Error Handling Strategies
**Files:** Various gateways
- `MelliGateway.cs:89` throws `GatewayCommunicationException` on null response
- Others return `GatewayOperationResult(false, ...)` 
- Mix of `LogWarning`, `LogError`, `LogCritical` for similar failures
- No consistent retry/Polly policies for transient failures

**Fix:** 
- Standardize: gateways never throw for expected bank errors; only throw for infrastructure failures (network, serialization)
- Add `GatewayResiliencePolicy` (Polly) registered in DI per gateway
- Use `ILogger` levels consistently: Warning for bank-declined, Error for infrastructure, Critical for data integrity (amount mismatch)

---

### 7. Commented-Out Validation Code in `CreatePaymentAsync`
**File:** `PaymentOrchestrationService.cs:43-56`
```csharp
//if (!ReturnUrlValidator.IsAllowed(request.ReturnUrl, _callbackOptions))
//    throw new ValidationException(...);
//if ((hasSplits || request.TerminalNumber is not null)
//    && request.GatewayProvider != GatewayProvider.Pasargad)
//    throw new ValidationException(...);
```

**Issue:** Dead code creates confusion; validation either needed or not.

**Fix:** Remove or re-enable with proper feature flags/configuration.

---

### 8. Inconsistent `CancellationToken` Usage
**Files:**
- `PaymentOrchestrationService.cs:145-146` uses `CancellationToken.None` in catch block
- `PaymentOrchestrationService.cs:310` catches `ConcurrencyException` but doesn't pass token

**Fix:** Always pass `cancellationToken`; use `CancellationToken.None` only for fire-and-forget compensation actions with clear comment.

---

## Priority 3: Medium (DI/Configuration)

### 9. Tight Coupling in `ServiceCollectionExtensions.cs`
**File:** `ServiceCollectionExtensions.cs:58-64`
```csharp
services.AddMellatGateway(configuration);
services.AddAsanPardakhtGateway(configuration);
services.AddParsianGateway(configuration);
services.AddSamanGateway(configuration);
services.AddPardakhtNovinGateway(configuration);
services.AddMelliGateway(configuration);
services.AddPasargadGateway(configuration);
```

**Issue:** Must modify this file to add/remove gateways; violates OCP.

**Fix:** Use assembly scanning or `IConfiguration`-driven registration:
```csharp
services.AddGatewaysFromConfiguration(configuration);
// Or: services.Scan(scan => scan.FromAssembliesOf<IPaymentGateway>().AddClasses...
```

---

### 10. Duplicated Options Registration Pattern
**Files:** Every gateway's `*ServiceCollectionExtensions.cs`
```csharp
services.Configure<MellatOptions>(configuration.GetSection(MellatOptions.SectionName));
services.AddSingleton(sp => sp.GetRequiredService<IOptions<MellatOptions>>().Value);
services.AddHttpClient<MellatSoapClient>().UseGatewayPrimaryHandler(configuration);
services.AddScoped<IPaymentGateway, MellatGateway>();
```

**Fix:** Create `AddGateway<TOptions, TClient, TGateway>()` extension method.

---

### 11. `SamanGateway` Injects `IPaymentTransactionRepository` Directly
**File:** `SamanGateway.cs:15, 19-25`
```csharp
private readonly IPaymentTransactionRepository _repository;
public SamanGateway(..., IPaymentTransactionRepository repository) { ... }
```

**Issue:** Gateway should not depend on persistence abstraction; violates layer separation. Only `SamanGateway` does this.

**Fix:** Move any repository usage to orchestration layer; gateway should be stateless.

---

## Priority 4: Low (Naming/Consistency)

### 12. Namespace/Class Naming Inconsistency: `MelliGateway` in `Sedad` Namespace
**File:** `MelliGateway.cs:7-10`
```csharp
namespace PaymentService.Infrastructure.Gateways.Sedad;
public class MelliGateway : IPaymentGateway
{
    public GatewayProvider Provider => GatewayProvider.Sadad;
}
```

**Issue:** Namespace `Sedad` ≠ class `MelliGateway` ≠ provider `Sadad`. Confusing.

**Fix:** Align: namespace `Melli`, class `MelliGateway`, provider `Melli` (or `Sadad` everywhere).

---

### 13. Mixed Property Names for Gateway References
**Domain:** `PaymentTransaction.cs`
- `GatewayRefId` (RefId/Token from initial request)
- `GatewaySaleReferenceId` (SaleReferenceId from callback/verify)
- `GatewayResultCode` (raw result code)

**Gateways use inconsistently:**
- Mellat: `GatewayRefId` = RefId, `GatewaySaleReferenceId` = SaleReferenceId
- Pasargad: `GatewayRefId` = UrlId, `GatewaySaleReferenceId` = referenceNumber/trackId
- Parsian: `GatewayRefId` = Token
- AsanPardakht: stores JSON in `GatewaySaleReferenceId`

**Fix:** Document each gateway's mapping; consider value objects `GatewayToken`, `GatewaySaleReference`.

---

### 14. `GatewayProvider` Enum Missing `IranKish` in `PaymentDisplayNames.cs`
**File:** `PaymentDisplayNames.cs:26`
```csharp
GatewayProvider.IranKish => "ایران کیش",
```
But `GatewayProvider` enum likely has more values (Sepehr at line 29 not in all gateways).

**Fix:** Ensure enum and display names stay in sync; consider `[Description]` attributes.

---

### 15. `PasargadGateway.VerifyAsync` Returns Success Without Actual Verification
**File:** `PasargadGateway.cs:80-82`
```csharp
public Task<GatewayOperationResult> VerifyAsync(...)
    => Task.FromResult(new GatewayOperationResult(true, "0", null));
```

**Issue:** Comment says "Confirm is final operation", but interface contract implies Verify should verify. Misleading for callers.

**Fix:** Either rename interface method to `PreSettleCheckAsync` or implement actual verification if possible.

---

## Summary: Suggested Refactor Order

| Order | Task | Effort | Impact |
|-------|------|--------|--------|
| 1 | Extract `GatewayParameterHelper.GetParameter` | Low | High (removes 8 duplicates) |
| 2 | Create `IGatewayErrorMapper` + default implementation | Medium | High (consistent errors) |
| 3 | Split `PaymentOrchestrationService` into 5 focused services | High | High (SRP, testability) |
| 4 | Replace magic string callback URL with gateway-provided route | Low | Medium (decoupling) |
| 5 | Define `GatewayErrorCodes` constants | Low | Medium (consistency) |
| 6 | Standardize error handling + add Polly policies | Medium | High (reliability) |
| 7 | Remove commented validation code | Low | Low (cleanup) |
| 8 | Fix CancellationToken usage | Low | Medium (correctness) |
| 9 | Make gateway registration configuration-driven | Medium | Medium (extensibility) |
| 10 | Create generic `AddGateway<>()` extension | Low | Medium (DRY) |
| 11 | Remove repository from `SamanGateway` | Low | High (layer separation) |
| 12 | Align Melli/Sedad/Sadad naming | Low | Low (clarity) |
| 13 | Document/standardize gateway reference fields | Low | Medium (maintainability) |
| 14 | Sync enum/display names | Low | Low (consistency) |
| 15 | Fix `PasargadGateway.VerifyAsync` contract | Low | Medium (correctness) |