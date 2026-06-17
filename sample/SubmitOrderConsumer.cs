using System;
using System.Threading.Tasks;
using MassTransit;
using SampleApp.Contracts;

namespace SampleApp.Consumers;

public class SubmitOrderConsumer : IConsumer<SubmitOrderCommand>
{
    public async Task Consume(ConsumeContext<SubmitOrderCommand> context)
    {
        var command = context.Message;

        if (command.TotalAmount <= 0)
        {
            await context.Publish<OrderRejectedEvent>(new OrderRejectedEvent
            {
                OrderId = command.OrderId,
                Reason = "El monto debe ser mayor que cero."
            });
            return;
        }

        // Simular negocio y aceptar
        await context.Publish<OrderAcceptedEvent>(new OrderAcceptedEvent
        {
            OrderId = command.OrderId,
            AcceptedAt = DateTime.UtcNow
        });
    }
}
