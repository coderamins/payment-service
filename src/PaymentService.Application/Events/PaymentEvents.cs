namespace PaymentService.Application.Events;

public record PaymentSettledEvent(
    Guid TransactionId,
    string OrderId,
    long AmountInRials,
    string GatewayProvider,
    string? ReferenceCode,
    DateTime SettledAtUtc);

public record PaymentReservedEvent(
    Guid TransactionId,
    string OrderId,
    long AmountInRials,
    DateTime ReservedAtUtc);
