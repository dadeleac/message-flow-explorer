using System;
using System.Threading;
using System.Threading.Tasks;

namespace SampleApp.MediatR;

public interface IRequest<out TResponse> { }
public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

public interface INotification { }
public interface INotificationHandler<in TNotification> where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}

public interface IMediator
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
    Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification;
}

public class CreateInvoiceHandler : IRequestHandler<CreateInvoiceCommand, CreateInvoiceResponse>
{
    private readonly IMediator _mediator;

    public CreateInvoiceHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<CreateInvoiceResponse> Handle(CreateInvoiceCommand request, CancellationToken cancellationToken)
    {
        // Lógica de creación de factura...
        var invoiceId = Guid.NewGuid();

        // Notificar que la factura fue creada (MediatR in-process)
        await _mediator.Publish(new InvoiceCreatedNotification
        {
            InvoiceId = invoiceId,
            OrderId = request.OrderId
        });

        return new CreateInvoiceResponse { InvoiceId = invoiceId, Success = true };
    }
}

public class InvoiceCreatedEmailHandler : INotificationHandler<InvoiceCreatedNotification>
{
    public Task Handle(InvoiceCreatedNotification notification, CancellationToken cancellationToken)
    {
        // Enviar email de factura creada
        return Task.CompletedTask;
    }
}
