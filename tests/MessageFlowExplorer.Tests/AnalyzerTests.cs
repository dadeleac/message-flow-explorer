using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MessageFlowExplorer.Analyzer;
using Xunit;

namespace MessageFlowExplorer.Tests;

public class AnalyzerTests
{
    private (SemanticModel, SyntaxTree) CreateSemanticModel(string sourceCode)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        
        var compilation = CSharpCompilation.Create("TestAssembly")
            .AddSyntaxTrees(syntaxTree)
            .AddReferences(
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "netstandard.dll")),
                MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Collections.dll"))
            );

        return (compilation.GetSemanticModel(syntaxTree), syntaxTree);
    }

    [Fact]
    public void Should_Discover_Basic_Consumer()
    {
        // Arrange
        var code = @"
using System.Threading.Tasks;

namespace TestNamespace;

public interface IConsumer<T> where T : class
{
    Task Consume(T context);
}

public class OrderCreated { }

public class OrderCreatedConsumer : IConsumer<OrderCreated>
{
    public Task Consume(OrderCreated context) => Task.CompletedTask;
}
";
        var (semanticModel, syntaxTree) = CreateSemanticModel(code);
        var walker = new MassTransitSyntaxWalker(semanticModel);

        // Act
        walker.Visit(syntaxTree.GetRoot());

        // Assert
        Assert.Single(walker.Consumers);
        var consumer = walker.Consumers[0];
        Assert.Equal("TestNamespace.OrderCreatedConsumer", consumer.ConsumerType);
        Assert.Equal("TestNamespace.OrderCreated", consumer.MessageType);
    }

    [Fact]
    public void Should_Discover_Publish_Calls()
    {
        // Arrange
        var code = @"
using System.Threading.Tasks;

namespace TestNamespace;

public interface IPublishEndpoint
{
    Task Publish<T>(T message);
}

public class OrderCreated { }

public class OrderService
{
    private readonly IPublishEndpoint _publishEndpoint;

    public OrderService(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public async Task CreateOrder()
    {
        await _publishEndpoint.Publish<OrderCreated>(new OrderCreated());
    }
}
";
        var (semanticModel, syntaxTree) = CreateSemanticModel(code);
        var walker = new MassTransitSyntaxWalker(semanticModel);

        // Act
        walker.Visit(syntaxTree.GetRoot());

        // Assert
        Assert.Single(walker.Producers);
        var producer = walker.Producers[0];
        Assert.Equal("TestNamespace.OrderCreated", producer.MessageType);
        Assert.Equal("Publish", producer.CallType);
    }

    [Fact]
    public void Should_Discover_NonGeneric_Publish_Calls_With_Fallback()
    {
        // Arrange
        var code = @"
using System.Threading.Tasks;

namespace TestNamespace;

public interface IPublishEndpoint
{
    Task Publish(object message);
}

public class OrderCreated { }

public class OrderService
{
    private readonly IPublishEndpoint _publishEndpoint;

    public OrderService(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public async Task CreateOrder()
    {
        await _publishEndpoint.Publish(new OrderCreated());
    }
}
";
        var (semanticModel, syntaxTree) = CreateSemanticModel(code);
        var walker = new MassTransitSyntaxWalker(semanticModel);

        // Act
        walker.Visit(syntaxTree.GetRoot());

        // Assert
        Assert.Single(walker.Producers);
        var producer = walker.Producers[0];
        Assert.Equal("TestNamespace.OrderCreated", producer.MessageType);
        Assert.Equal("Publish", producer.CallType);
    }

    [Fact]
    public void Should_Discover_Saga_StateMachine()
    {
        // Arrange
        var code = @"
using System;
using System.Threading.Tasks;

namespace TestNamespace;

public class Event<T> { }
public class OrderSubmitted { }
public class OrderProcessed { }

public class MassTransitStateMachine<T> { }

public class OrderStateMachineSaga : MassTransitStateMachine<OrderStateMachineSaga>
{
    public Event<OrderSubmitted> OrderSubmitted { get; private set; }
    
    public OrderStateMachineSaga()
    {
        // Simular configuración fluida mediante acceso a miembro
        this.Publish(new OrderProcessed());
    }

    private void Publish<T>(T msg) { }
}
";
        var (semanticModel, syntaxTree) = CreateSemanticModel(code);
        var walker = new MassTransitSyntaxWalker(semanticModel);

        // Act
        walker.Visit(syntaxTree.GetRoot());

        // Assert
        Assert.Single(walker.Sagas);
        var saga = walker.Sagas[0];
        Assert.Equal("TestNamespace.OrderStateMachineSaga", saga.SagaType);
        Assert.Contains("TestNamespace.OrderSubmitted", saga.ConsumedEvents);
        Assert.Contains("TestNamespace.OrderProcessed", saga.PublishedMessages);
    }

    [Fact]
    public void Should_Discover_Activity()
    {
        // Arrange
        var code = @"
using System;
using System.Threading.Tasks;

namespace TestNamespace;

public interface IActivity<TExecute, TCompensate> { }
public class ProcessPaymentArguments { }
public class ProcessPaymentLog { }

public class ProcessPaymentActivity : IActivity<ProcessPaymentArguments, ProcessPaymentLog>
{
}
";
        var (semanticModel, syntaxTree) = CreateSemanticModel(code);
        var walker = new MassTransitSyntaxWalker(semanticModel);

        // Act
        walker.Visit(syntaxTree.GetRoot());

        // Assert
        Assert.Single(walker.Activities);
        var activity = walker.Activities[0];
        Assert.Equal("TestNamespace.ProcessPaymentActivity", activity.ActivityType);
        Assert.Equal("TestNamespace.ProcessPaymentArguments", activity.ArgumentsType);
        Assert.Equal("TestNamespace.ProcessPaymentLog", activity.CompensateLogType);
    }

    [Fact]
    public void Should_Discover_MediatR_Handler_And_Send_Calls()
    {
        // Arrange
        var code = @"
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TestNamespace;

public interface IRequest<out TResponse> { }
public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

public interface IMediator
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}

public class CreateOrder : IRequest<Guid> { }

public class CreateOrderHandler : IRequestHandler<CreateOrder, Guid>
{
    public Task<Guid> Handle(CreateOrder request, CancellationToken cancellationToken) => Task.FromResult(Guid.NewGuid());
}

public class OrderController
{
    private readonly IMediator _mediator;

    public OrderController(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task Create()
    {
        await _mediator.Send(new CreateOrder());
    }
}
";
        var (semanticModel, syntaxTree) = CreateSemanticModel(code);
        var walker = new MassTransitSyntaxWalker(semanticModel);

        // Act
        walker.Visit(syntaxTree.GetRoot());

        // Assert
        // 1. Verificar el Handler (Consumer)
        Assert.Single(walker.Consumers);
        var consumer = walker.Consumers[0];
        Assert.Equal("TestNamespace.CreateOrderHandler", consumer.ConsumerType);
        Assert.Equal("TestNamespace.CreateOrder", consumer.MessageType);
        Assert.Equal("MediatR", consumer.Provider);

        // 2. Verificar el Publisher (Producer)
        Assert.Single(walker.Producers);
        var producer = walker.Producers[0];
        Assert.Equal("TestNamespace.CreateOrder", producer.MessageType);
        Assert.Equal("Send", producer.CallType);
        Assert.Equal("MediatR", producer.Provider);
    }

    private const string RoutingSlipStubs = @"
using System;
namespace MassTransit
{
    public enum RoutingSlipEventsKind { Completed, Faulted }
    public static class RoutingSlipEvents { public static RoutingSlipEventsKind Completed; public static RoutingSlipEventsKind Faulted; }
    public class RoutingSlip { }
    public class RoutingSlipBuilder
    {
        public RoutingSlipBuilder(Guid id) { }
        public RoutingSlipBuilder AddActivity(string name, Uri address, object args) => this;
        public RoutingSlipBuilder AddSubscription(Uri address, RoutingSlipEventsKind events) => this;
        public RoutingSlip Build() => new RoutingSlip();
    }
    public interface IBus { System.Threading.Tasks.Task Execute(RoutingSlip slip); }
    public static class NewId { public static Guid NextGuid() => Guid.NewGuid(); }
}
";

    [Fact]
    public void Should_Discover_RoutingSlip_Itinerary_In_Order()
    {
        var code = RoutingSlipStubs + @"
namespace Demo;
using MassTransit;
using System;
using System.Threading.Tasks;
public class PayArgs { }
public class ShipArgs { }
public class Fulfillment
{
    private readonly IBus _bus;
    public Fulfillment(IBus bus) { _bus = bus; }
    public async Task Run(Guid id)
    {
        var builder = new RoutingSlipBuilder(NewId.NextGuid());
        builder.AddActivity(""Payment"", new Uri(""queue:pay""), new PayArgs());
        builder.AddActivity(""Shipping"", new Uri(""queue:ship""), new ShipArgs());
        builder.AddSubscription(new Uri(""queue:ev""), RoutingSlipEvents.Completed);
        var slip = builder.Build();
        await _bus.Execute(slip);
    }
}
";
        var (semanticModel, syntaxTree) = CreateSemanticModel(code);
        var walker = new MassTransitSyntaxWalker(semanticModel);
        walker.Visit(syntaxTree.GetRoot());

        Assert.Single(walker.RoutingSlips);
        var slip = walker.RoutingSlips[0];
        Assert.Equal(new[] { "Payment", "Shipping" }, slip.Itinerary.Select(s => s.Name).ToArray());
        Assert.Equal("Demo.PayArgs", slip.Itinerary[0].ArgumentsType);
        Assert.Contains("Completed", slip.SubscribedEvents);
        Assert.DoesNotContain("Faulted", slip.SubscribedEvents);
        Assert.True(slip.IsExecuted);
    }

    [Fact]
    public void Should_Flag_RoutingSlip_That_Is_Not_Executed()
    {
        var code = RoutingSlipStubs + @"
namespace Demo;
using MassTransit;
using System;
public class PayArgs { }
public class Fulfillment
{
    public RoutingSlip Build(Guid id)
    {
        var builder = new RoutingSlipBuilder(NewId.NextGuid());
        builder.AddActivity(""Payment"", new Uri(""queue:pay""), new PayArgs());
        return builder.Build();
    }
}
";
        var (semanticModel, syntaxTree) = CreateSemanticModel(code);
        var walker = new MassTransitSyntaxWalker(semanticModel);
        walker.Visit(syntaxTree.GetRoot());

        Assert.Single(walker.RoutingSlips);
        Assert.False(walker.RoutingSlips[0].IsExecuted);
        Assert.Empty(walker.RoutingSlips[0].SubscribedEvents);
    }

    [Fact]
    public void Walker_Should_Honor_ProjectName_Override()
    {
        // Arrange: simulamos el caso en que MSBuild ya nos da el nombre real del proyecto.
        var code = @"
namespace TestNamespace;
public interface IConsumer<T> where T : class { }
public class OrderCreated { }
public class OrderCreatedConsumer : IConsumer<OrderCreated> { }
";
        var (semanticModel, syntaxTree) = CreateSemanticModel(code);
        var walker = new MassTransitSyntaxWalker(semanticModel, scanRoot: "", projectNameOverride: "Orders.Api");

        // Act
        walker.Visit(syntaxTree.GetRoot());

        // Assert
        Assert.Single(walker.Consumers);
        Assert.Equal("Orders.Api", walker.Consumers[0].Project);
    }

    [Fact]
    public void ScanDirectory_Without_Csproj_Falls_Back_To_AdHoc_Analysis()
    {
        // Arrange: carpeta con .cs sueltos y SIN .csproj -> debe usar el modo de respaldo.
        var tempDir = Path.Combine(Path.GetTempPath(), "mfe-adhoc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "Consumer.cs"), @"
namespace Demo;
public interface IConsumer<T> where T : class { }
public class OrderCreated { }
public class OrderConsumer : IConsumer<OrderCreated> { }
");

            var scanner = new SolutionScanner();

            // Act
            var report = scanner.ScanDirectory(tempDir);

            // Assert
            Assert.Single(report.Consumers);
            Assert.Equal("Demo.OrderConsumer", report.Consumers[0].ConsumerType);
            Assert.Contains(scanner.Diagnostics, d => d.Contains("sintáctico"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
