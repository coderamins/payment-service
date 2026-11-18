namespace PaymentService.Application.Common.Exceptions;

public class ConcurrencyException : ConflictException
{
    public ConcurrencyException(string message = "این تراکنش در حال پردازش دیگری است.")
        : base(message) { }
}