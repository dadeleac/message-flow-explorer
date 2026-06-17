using System.Threading.Tasks;
using MassTransit;
using SampleApp.Contracts;

namespace SampleApp.Consumers;

public class OrderStatusConsumer : IConsumer<CheckOrderStatusRequest>
{
    public async Task Consume(ConsumeContext<CheckOrderStatusRequest> context)
    {
        var request = context.Message;

        // Responder a la solicitud
        await context.RespondAsync<OrderStatusResponse>(new OrderStatusResponse
        {
            OrderId = request.OrderId,
            Status = "Completed"
        });
    }
}
