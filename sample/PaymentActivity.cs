using System.Threading.Tasks;
using MassTransit;
using SampleApp.Contracts;

namespace SampleApp.Activities;

public class PaymentActivity : IExecuteActivity<PaymentArguments>
{
    public async Task<ExecutionResult> Execute(ExecuteContext<PaymentArguments> context)
    {
        // Simular ejecución del pago
        return context.Completed();
    }
}
