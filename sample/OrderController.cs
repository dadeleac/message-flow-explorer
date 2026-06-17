using System;
using System.Threading.Tasks;
using MassTransit;
using SampleApp.Contracts;

namespace SampleApp.Controllers;

public class OrderController
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IRequestClient<CheckOrderStatusRequest> _client;

    public OrderController(IPublishEndpoint publishEndpoint, IRequestClient<CheckOrderStatusRequest> client)
    {
        _publishEndpoint = publishEndpoint;
        _client = client;
    }

    public async Task CreateOrder(Guid orderId, string customerId, decimal amount)
    {
        // Publicar comando
        await _publishEndpoint.Publish<SubmitOrderCommand>(new SubmitOrderCommand
        {
            OrderId = orderId,
            CustomerId = customerId,
            TotalAmount = amount
        });
    }

    public async Task<string> CheckStatus(Guid orderId)
    {
        // Enviar petición / request-response
        var response = await _client.GetResponse<OrderStatusResponse>(new CheckOrderStatusRequest
        {
            OrderId = orderId
        });

        return response.Message.Status;
    }
}
