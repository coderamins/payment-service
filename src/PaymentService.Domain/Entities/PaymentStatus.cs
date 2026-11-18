namespace PaymentService.Domain.Entities;

public class PaymentStatus
{
    public int PaymentStatusId { get; set; }
    public string StatusDesc { get; private set; } = string.Empty;

}
