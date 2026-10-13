namespace PaymentService.Application.Events;

public record PaymentSettledEvent(
    long TransactionId,
    long OrderId,
    long AmountInRials,
    string GatewayProvider,
    string? ReferenceCode,
    DateTime SettledAtUtc);

public record PaymentReversedEvent(
    long TransactionId,
    long OrderId,
    long AmountInRials,
    DateTime ReversedAtUtc);
