using System;
using MassTransit;
using SampleApp.Contracts;

namespace SampleApp.Sagas;

public class OrderSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; }
}

public class OrderSaga : MassTransitStateMachine<OrderSagaState>
{
    public Event<SubmitOrderCommand> OrderSubmitted { get; private set; }
    public Event<OrderAcceptedEvent> OrderAccepted { get; private set; }

    public OrderSaga()
    {
        // Simular la configuración fluida
        this.Publish(new OrderAcceptedEvent
        {
            OrderId = Guid.NewGuid(),
            AcceptedAt = DateTime.UtcNow
        });
    }
}
