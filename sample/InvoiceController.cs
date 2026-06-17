using System;
using System.Threading.Tasks;

namespace SampleApp.MediatR;

public class InvoiceController
{
    private readonly IMediator _mediator;

    public InvoiceController(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<CreateInvoiceResponse> CreateInvoice(Guid orderId, decimal amount)
    {
        var response = await _mediator.Send(new CreateInvoiceCommand
        {
            OrderId = orderId,
            Amount = amount,
            CustomerEmail = "customer@example.com"
        });

        return response;
    }
}
