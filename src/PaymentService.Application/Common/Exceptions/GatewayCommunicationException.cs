namespace PaymentService.Application.Common.Exceptions
{
    public sealed class GatewayCommunicationException : AppException
    {
        public GatewayCommunicationException(string message)
            : base(message)
        {
        }
    }
}
