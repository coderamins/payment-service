namespace PaymentService.Application.Events;

public record PaymentSettledEvent(
    long TransactionId,
    long OrderId,
    decimal AmountInRials,
    string GatewayProvider,
    string? ReferenceCode,
    DateTime SettledAtUtc);

public record PaymentReversedEvent(
    long TransactionId,
    long OrderId,
    decimal AmountInRials,
    DateTime ReversedAtUtc);

public record PaymentVerifiedEvent(
    long TransactionId, long OrderId, decimal AmountInRials,
    string GatewayProvider, string? ReferenceCode, DateTime VerifiedAtUtc);

public record PaymentCanceledEvent(
    long TransactionId, long OrderId, decimal AmountInRials,
    string GatewayProvider, DateTime CanceledAtUtc);

public record PaymentFailedEvent(
    long TransactionId, long OrderId, decimal AmountInRials,
    string GatewayProvider, string Stage, string? Reason, DateTime FailedAtUtc);

public record PaymentAmountMismatchEvent(
     long TransactionId, long OrderId,decimal AmountInRials, decimal ReportedAmountInRials,
    string GatewayProvider, DateTime CanceledAtUtc);