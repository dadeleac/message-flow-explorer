using System;

namespace SampleApp.MediatR;

// MediatR Commands
public class CreateInvoiceCommand
{
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public string CustomerEmail { get; set; }
}

public class CreateInvoiceResponse
{
    public Guid InvoiceId { get; set; }
    public bool Success { get; set; }
}

// MediatR Notifications
public class InvoiceCreatedNotification
{
    public Guid InvoiceId { get; set; }
    public Guid OrderId { get; set; }
}
