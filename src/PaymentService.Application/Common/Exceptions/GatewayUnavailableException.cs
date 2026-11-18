namespace PaymentService.Application.Common.Exceptions
{
    public sealed class GatewayUnavailableException : AppException
    {
        public GatewayUnavailableException(string message)
            : base(message)
        {
        }
    }
}
