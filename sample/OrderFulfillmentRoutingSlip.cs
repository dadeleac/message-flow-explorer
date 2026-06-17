using System;
using System.Threading.Tasks;
using MassTransit;
using SampleApp.Contracts;

namespace SampleApp.Routing;

// Argumentos de las actividades del itinerario.
public class ShippingArguments
{
    public Guid OrderId { get; set; }
    public string Address { get; set; }
}

public class NotificationArguments
{
    public Guid OrderId { get; set; }
}

// Construye y ejecuta una routing slip de cumplimiento de pedido:
// Pago -> Envío -> Notificación. Se suscribe a Completed pero NO a Faulted
// (a propósito, para que la herramienta muestre la advertencia de fallo no manejado).
public class OrderFulfillmentRoutingSlip
{
    private readonly IBus _bus;

    public OrderFulfillmentRoutingSlip(IBus bus) => _bus = bus;

    public async Task Execute(Guid orderId, decimal amount, string address)
    {
        var builder = new RoutingSlipBuilder(NewId.NextGuid());

        builder.AddActivity("Payment", new Uri("queue:payment_execute"), new PaymentArguments
        {
            OrderId = orderId,
            Amount = amount
        });

        builder.AddActivity("Shipping", new Uri("queue:shipping_execute"), new ShippingArguments
        {
            OrderId = orderId,
            Address = address
        });

        builder.AddActivity("Notification", new Uri("queue:notification_execute"), new NotificationArguments
        {
            OrderId = orderId
        });

        builder.AddSubscription(new Uri("queue:order_events"), RoutingSlipEvents.Completed);

        var routingSlip = builder.Build();
        await _bus.Execute(routingSlip);
    }
}
