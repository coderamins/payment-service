namespace PaymentService.Application.Common.Exceptions
{
    public sealed class UnsupportedGatewayException : AppException
    {
        public UnsupportedGatewayException(string message)
            : base(message)
        {
        }
    }
}
