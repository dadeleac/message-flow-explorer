using System;

namespace SampleApp.Contracts;

public class SubmitOrderCommand
{
    public Guid OrderId { get; set; }
    public string CustomerId { get; set; }
    public decimal TotalAmount { get; set; }
}

public class OrderAcceptedEvent
{
    public Guid OrderId { get; set; }
    public DateTime AcceptedAt { get; set; }
}

public class OrderRejectedEvent
{
    public Guid OrderId { get; set; }
    public string Reason { get; set; }
}

public class CheckOrderStatusRequest
{
    public Guid OrderId { get; set; }
}

public class OrderStatusResponse
{
    public Guid OrderId { get; set; }
    public string Status { get; set; }
}

public class PaymentArguments
{
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public string CardNumber { get; set; }
}
