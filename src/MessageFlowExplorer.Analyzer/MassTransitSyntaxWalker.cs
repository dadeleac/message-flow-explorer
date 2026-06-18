using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MessageFlowExplorer.Core;

namespace MessageFlowExplorer.Analyzer;

public class MassTransitSyntaxWalker : CSharpSyntaxWalker
{
    private readonly SemanticModel _semanticModel;
    private readonly string _scanRoot;
    private readonly string? _projectNameOverride;
    public List<ProducerInfo> Producers { get; } = new();
    public List<ConsumerInfo> Consumers { get; } = new();
    public List<SagaInfo> Sagas { get; } = new();
    public List<ActivityInfo> Activities { get; } = new();
    public List<RoutingSlipInfo> RoutingSlips { get; } = new();

    private bool _inSaga = false;
    private string? _currentSagaName;
    private string? _currentSagaLocation;
    private string? _currentSagaProject;
    private List<string> _currentSagaConsumed = new();
    private List<string> _currentSagaPublished = new();

    public MassTransitSyntaxWalker(SemanticModel semanticModel, string scanRoot = "", string? projectNameOverride = null)
    {
        _semanticModel = semanticModel;
        _scanRoot = scanRoot;
        _projectNameOverride = projectNameOverride;
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        CheckConsumerDeclaration(node);
        CheckActivityDeclaration(node);

        bool parentInSaga = _inSaga;
        string? parentSagaName = _currentSagaName;
        string? parentSagaLocation = _currentSagaLocation;
        string? parentSagaProject = _currentSagaProject;
        List<string> parentSagaConsumed = _currentSagaConsumed;
        List<string> parentSagaPublished = _currentSagaPublished;

        if (IsSagaClass(node))
        {
            _inSaga = true;
            _currentSagaLocation = GetLocationDescription(node);
            _currentSagaProject = GetProjectName(node);
            
            var sagaTypeName = node.Identifier.Text;
            if (_semanticModel != null)
            {
                var classSymbol = _semanticModel.GetDeclaredSymbol(node);
                if (classSymbol != null)
                {
                    sagaTypeName = classSymbol.ToDisplayString();
                }
            }
            _currentSagaName = sagaTypeName;
            _currentSagaConsumed = new List<string>();
            _currentSagaPublished = new List<string>();
        }

        base.VisitClassDeclaration(node);

        if (_inSaga && !parentInSaga)
        {
            var codeSnippet = GetCodeSnippet(node);
            Sagas.Add(new SagaInfo(
                _currentSagaLocation!, 
                _currentSagaName!, 
                _currentSagaConsumed.Distinct().ToList(), 
                _currentSagaPublished.Distinct().ToList(),
                _currentSagaProject!,
                codeSnippet
            ));
        }

        _inSaga = parentInSaga;
        _currentSagaName = parentSagaName;
        _currentSagaLocation = parentSagaLocation;
        _currentSagaProject = parentSagaProject;
        _currentSagaConsumed = parentSagaConsumed;
        _currentSagaPublished = parentSagaPublished;
    }

    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        if (_inSaga)
        {
            var typeSyntax = node.Type;
            var genericName = GetGenericName(typeSyntax);
            if (genericName != null && genericName.Identifier.Text == "Event" && genericName.TypeArgumentList.Arguments.Count == 1)
            {
                var msgTypeSyntax = genericName.TypeArgumentList.Arguments[0];
                var msgTypeName = msgTypeSyntax.ToString();
                
                if (_semanticModel != null)
                {
                    var typeInfo = _semanticModel.GetTypeInfo(msgTypeSyntax);
                    if (typeInfo.Type != null && typeInfo.Type.Kind != SymbolKind.ErrorType)
                    {
                        msgTypeName = typeInfo.Type.ToDisplayString();
                    }
                }
                _currentSagaConsumed.Add(msgTypeName);
            }
        }
        base.VisitPropertyDeclaration(node);
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        CheckMessagePublication(node);
        base.VisitInvocationExpression(node);
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        DetectRoutingSlips(node);
        base.VisitMethodDeclaration(node);
    }

    public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        DetectRoutingSlips(node);
        base.VisitConstructorDeclaration(node);
    }

    private void CheckConsumerDeclaration(ClassDeclarationSyntax node)
    {
        if (node.BaseList == null) return;

        foreach (var baseTypeSyntax in node.BaseList.Types)
        {
            var typeSyntax = baseTypeSyntax.Type;
            var genericName = GetGenericName(typeSyntax);
            if (genericName != null)
            {
                var interfaceName = genericName.Identifier.Text;
                if (interfaceName == "IConsumer" && genericName.TypeArgumentList.Arguments.Count == 1)
                {
                    // Batch consumers: IConsumer<Batch<T>> consume realmente T.
                    var messageTypeSyntax = UnwrapBatch(genericName.TypeArgumentList.Arguments[0]);
                    var messageTypeName = GetTypeDisplayString(messageTypeSyntax);
                    var location = GetLocationDescription(node);
                    var consumerTypeName = GetClassDisplayString(node);
                    var project = GetProjectName(node);
                    var codeSnippet = GetCodeSnippet(node);
                    Consumers.Add(new ConsumerInfo(location, messageTypeName, consumerTypeName, project, codeSnippet, "MassTransit"));
                }
                else if (interfaceName == "IRequestHandler" && genericName.TypeArgumentList.Arguments.Count >= 1)
                {
                    var messageTypeSyntax = genericName.TypeArgumentList.Arguments[0];
                    var messageTypeName = GetTypeDisplayString(messageTypeSyntax);
                    var location = GetLocationDescription(node);
                    var consumerTypeName = GetClassDisplayString(node);
                    var project = GetProjectName(node);
                    var codeSnippet = GetCodeSnippet(node);
                    Consumers.Add(new ConsumerInfo(location, messageTypeName, consumerTypeName, project, codeSnippet, "MediatR"));
                }
                else if (interfaceName == "INotificationHandler" && genericName.TypeArgumentList.Arguments.Count == 1)
                {
                    var messageTypeSyntax = genericName.TypeArgumentList.Arguments[0];
                    var messageTypeName = GetTypeDisplayString(messageTypeSyntax);
                    var location = GetLocationDescription(node);
                    var consumerTypeName = GetClassDisplayString(node);
                    var project = GetProjectName(node);
                    var codeSnippet = GetCodeSnippet(node);
                    Consumers.Add(new ConsumerInfo(location, messageTypeName, consumerTypeName, project, codeSnippet, "MediatR"));
                }
            }
        }
    }

    /// <summary>Si el tipo es Batch&lt;T&gt; devuelve T; en caso contrario, el tipo original.</summary>
    private TypeSyntax UnwrapBatch(TypeSyntax typeSyntax)
    {
        var generic = GetGenericName(typeSyntax);
        if (generic != null && generic.Identifier.Text == "Batch" && generic.TypeArgumentList.Arguments.Count == 1)
        {
            return generic.TypeArgumentList.Arguments[0];
        }
        return typeSyntax;
    }

    private string GetTypeDisplayString(TypeSyntax typeSyntax)
    {
        var typeName = typeSyntax.ToString();
        if (_semanticModel != null)
        {
            var typeInfo = _semanticModel.GetTypeInfo(typeSyntax);
            if (typeInfo.Type != null && typeInfo.Type.Kind != SymbolKind.ErrorType)
            {
                typeName = typeInfo.Type.ToDisplayString();
            }
        }
        return typeName;
    }

    private string GetClassDisplayString(ClassDeclarationSyntax node)
    {
        var className = node.Identifier.Text;
        if (_semanticModel != null)
        {
            var classSymbol = _semanticModel.GetDeclaredSymbol(node);
            if (classSymbol != null)
            {
                className = classSymbol.ToDisplayString();
            }
        }
        return className;
    }

    private void CheckActivityDeclaration(ClassDeclarationSyntax node)
    {
        if (node.BaseList == null) return;

        foreach (var baseTypeSyntax in node.BaseList.Types)
        {
            var typeSyntax = baseTypeSyntax.Type;
            var genericName = GetGenericName(typeSyntax);
            if (genericName != null)
            {
                var interfaceName = genericName.Identifier.Text;
                if (interfaceName == "IExecuteActivity" && genericName.TypeArgumentList.Arguments.Count == 1)
                {
                    var argsTypeSyntax = genericName.TypeArgumentList.Arguments[0];
                    var argsTypeName = argsTypeSyntax.ToString();
                    if (_semanticModel != null)
                    {
                        var typeInfo = _semanticModel.GetTypeInfo(argsTypeSyntax);
                        if (typeInfo.Type != null && typeInfo.Type.Kind != SymbolKind.ErrorType)
                        {
                            argsTypeName = typeInfo.Type.ToDisplayString();
                        }
                    }
                    
                    var activityTypeName = node.Identifier.Text;
                    if (_semanticModel != null)
                    {
                        var classSymbol = _semanticModel.GetDeclaredSymbol(node);
                        if (classSymbol != null)
                        {
                            activityTypeName = classSymbol.ToDisplayString();
                        }
                    }

                    var project = GetProjectName(node);
                    var location = GetLocationDescription(node);
                    var codeSnippet = GetCodeSnippet(node);
                    Activities.Add(new ActivityInfo(location, activityTypeName, argsTypeName, null, project, codeSnippet));
                }
                else if (interfaceName == "IActivity" && genericName.TypeArgumentList.Arguments.Count == 2)
                {
                    var argsTypeSyntax = genericName.TypeArgumentList.Arguments[0];
                    var logTypeSyntax = genericName.TypeArgumentList.Arguments[1];
                    var argsTypeName = argsTypeSyntax.ToString();
                    var logTypeName = logTypeSyntax.ToString();
                    
                    if (_semanticModel != null)
                    {
                        var argsTypeInfo = _semanticModel.GetTypeInfo(argsTypeSyntax);
                        if (argsTypeInfo.Type != null && argsTypeInfo.Type.Kind != SymbolKind.ErrorType)
                        {
                            argsTypeName = argsTypeInfo.Type.ToDisplayString();
                        }
                        var logTypeInfo = _semanticModel.GetTypeInfo(logTypeSyntax);
                        if (logTypeInfo.Type != null && logTypeInfo.Type.Kind != SymbolKind.ErrorType)
                        {
                            logTypeName = logTypeInfo.Type.ToDisplayString();
                        }
                    }
                    
                    var activityTypeName = node.Identifier.Text;
                    if (_semanticModel != null)
                    {
                        var classSymbol = _semanticModel.GetDeclaredSymbol(node);
                        if (classSymbol != null)
                        {
                            activityTypeName = classSymbol.ToDisplayString();
                        }
                    }

                    var project = GetProjectName(node);
                    var location = GetLocationDescription(node);
                    var codeSnippet = GetCodeSnippet(node);
                    Activities.Add(new ActivityInfo(location, activityTypeName, argsTypeName, logTypeName, project, codeSnippet));
                }
            }
        }
    }

    private bool IsSagaClass(ClassDeclarationSyntax node)
    {
        if (node.BaseList == null) return false;
        return node.BaseList.Types.Any(t => {
            var name = GetSimpleTypeName(t.Type);
            return name == "MassTransitStateMachine" || name.Contains("MassTransitStateMachine") || name == "ISaga" || name.EndsWith("Saga");
        });
    }

    private string GetSimpleTypeName(TypeSyntax typeSyntax)
    {
        if (typeSyntax is GenericNameSyntax gns) return gns.Identifier.Text;
        if (typeSyntax is IdentifierNameSyntax ins) return ins.Identifier.Text;
        if (typeSyntax is QualifiedNameSyntax qns) return GetSimpleTypeName(qns.Right);
        return typeSyntax.ToString();
    }

    private void CheckMessagePublication(InvocationExpressionSyntax node)
    {
        if (node.Expression is not MemberAccessExpressionSyntax memberAccess) return;

        var methodName = memberAccess.Name.Identifier.Text;
        string? callType = GetCallTypeFromMethodName(methodName);
        if (callType == null) return;

        string? messageTypeName = null;
        string? responseType = null;

        var genericArg0 = (memberAccess.Name is GenericNameSyntax gn && gn.TypeArgumentList.Arguments.Count >= 1)
            ? gn.TypeArgumentList.Arguments[0]
            : null;
        var argType = node.ArgumentList.Arguments.Count >= 1
            ? GetExpressionTypeName(node.ArgumentList.Arguments[0].Expression)
            : null;

        if (callType == "Request")
        {
            // Request/response, p. ej. client.GetResponse<TResponse>(new TRequest()).
            // El MENSAJE QUE SE ENVÍA es el argumento (la petición); el genérico es la
            // RESPUESTA esperada. (Antes se tomaba el genérico como mensaje, lo que
            // registraba erróneamente la respuesta como mensaje publicado.)
            messageTypeName = argType;
            if (genericArg0 != null)
            {
                responseType = GetTypeDisplayString(genericArg0);
            }
            // Degradación: si no hay argumento legible, usar el genérico como mensaje.
            if (messageTypeName == null && genericArg0 != null)
            {
                messageTypeName = GetTypeDisplayString(genericArg0);
                responseType = null;
            }
        }
        else
        {
            // Publish/Send/Respond: genérico primero, si no, el argumento.
            messageTypeName = genericArg0 != null ? GetTypeDisplayString(genericArg0) : argType;
        }

        if (messageTypeName != null)
        {
            var provider = DetermineProvider(memberAccess.Expression);
            var project = GetProjectName(node);
            var location = GetLocationDescription(node);
            var codeSnippet = GetCodeSnippet(node);
            Producers.Add(new ProducerInfo(location, messageTypeName, callType, project, codeSnippet, provider, responseType));

            if (_inSaga && provider == "MassTransit")
            {
                _currentSagaPublished.Add(messageTypeName);
            }

            // Cierre del bucle request/response: el solicitante RECIBE la respuesta.
            // Lo modelamos como un consumidor de la respuesta en la clase que llama,
            // de modo que la respuesta no aparezca como mensaje huérfano y se dibuje
            // la arista de vuelta.
            if (callType == "Request" && responseType != null)
            {
                var requesterClass = node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                var requesterName = requesterClass != null ? GetClassDisplayString(requesterClass) : "Requester";
                Consumers.Add(new ConsumerInfo(location, responseType, requesterName, project, codeSnippet, provider));
            }
        }
    }

    /// <summary>Resuelve el nombre del tipo de una expresión (semántico, con fallback sintáctico).</summary>
    private string? GetExpressionTypeName(ExpressionSyntax expr)
    {
        if (_semanticModel != null)
        {
            var typeInfo = _semanticModel.GetTypeInfo(expr);
            if (typeInfo.Type != null && typeInfo.Type.Kind != SymbolKind.ErrorType)
            {
                return typeInfo.Type.ToDisplayString();
            }
        }
        if (expr is ObjectCreationExpressionSyntax objCreation) return objCreation.Type.ToString();
        if (expr is IdentifierNameSyntax identifier) return identifier.Identifier.Text;
        return null;
    }

    // ------------------------------------------------------------------
    // Detección de Routing Slips (RoutingSlipBuilder)
    // ------------------------------------------------------------------
    private void DetectRoutingSlips(SyntaxNode scope)
    {
        var body = (SyntaxNode?)(scope as MethodDeclarationSyntax)?.Body
                   ?? (scope as MethodDeclarationSyntax)?.ExpressionBody
                   ?? (SyntaxNode?)(scope as ConstructorDeclarationSyntax)?.Body
                   ?? (scope as ConstructorDeclarationSyntax)?.ExpressionBody;
        if (body == null) return;

        var invocations = body.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();

        // ¿Hay alguna construcción de RoutingSlipBuilder en este método? Si no, salimos rápido.
        bool hasBuilder = body.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .Any(oc => GetSimpleTypeName(oc.Type).Contains("RoutingSlipBuilder"))
            || invocations.Any(inv => IsRoutingSlipBuilderCall(inv, out _, out _));
        if (!hasBuilder) return;

        // Agrupamos pasos/suscripciones por la variable builder (o un grupo único si se
        // construye de forma encadenada: builder.AddActivity(..).AddActivity(..)).
        var steps = new Dictionary<string, List<(int order, RoutingSlipStep step)>>();
        var subscriptions = new Dictionary<string, List<string>>();
        const string chainKey = "__chain__";

        bool isExecuted = false;

        foreach (var inv in invocations)
        {
            if (inv.Expression is not MemberAccessExpressionSyntax member) continue;
            var methodName = member.Name.Identifier.Text;

            // Ejecución: bus.Execute(routingSlip) / ExecuteAsync(...) con un arg de tipo RoutingSlip
            if (methodName is "Execute" or "ExecuteAsync")
            {
                if (inv.ArgumentList.Arguments.Any(a => IsRoutingSlipType(a.Expression)))
                {
                    isExecuted = true;
                }
                continue;
            }

            if (!IsRoutingSlipBuilderCall(inv, out var builderKey, out _)) continue;
            builderKey ??= chainKey;

            int order = member.Name.SpanStart;

            if (methodName is "AddActivity" or "AddActivityActivity")
            {
                var step = ExtractStep(inv, member);
                if (step != null)
                {
                    if (!steps.TryGetValue(builderKey, out var list)) steps[builderKey] = list = new();
                    list.Add((order, step));
                }
            }
            else if (methodName == "AddSubscription")
            {
                var ev = ExtractSubscriptionEvent(inv);
                if (ev != null)
                {
                    if (!subscriptions.TryGetValue(builderKey, out var list)) subscriptions[builderKey] = list = new();
                    if (!list.Contains(ev)) list.Add(ev);
                }
            }
        }

        // Unificamos las claves vistas (un builder puede tener pasos y/o suscripciones).
        var allKeys = new HashSet<string>(steps.Keys);
        allKeys.UnionWith(subscriptions.Keys);
        if (allKeys.Count == 0) return;

        var location = GetLocationDescription(scope);
        var project = GetProjectName(scope);
        var codeSnippet = GetCodeSnippet(scope);

        foreach (var key in allKeys)
        {
            var itinerary = steps.TryGetValue(key, out var s)
                ? s.OrderBy(x => x.order).Select(x => x.step).ToList()
                : new List<RoutingSlipStep>();
            var subs = subscriptions.TryGetValue(key, out var sub) ? sub : new List<string>();

            RoutingSlips.Add(new RoutingSlipInfo(location, itinerary, subs, isExecuted, project, codeSnippet));
        }
    }

    /// <summary>Determina si la invocación se hace sobre un RoutingSlipBuilder y, si es una
    /// variable identificable, devuelve una clave estable para agruparla.</summary>
    private bool IsRoutingSlipBuilderCall(InvocationExpressionSyntax inv, out string? builderKey, out string? builderType)
    {
        builderKey = null;
        builderType = null;
        if (inv.Expression is not MemberAccessExpressionSyntax member) return false;

        var receiver = member.Expression;

        // 1. Vía semántica (preferida cuando MSBuild resolvió las referencias).
        if (_semanticModel != null)
        {
            var typeInfo = _semanticModel.GetTypeInfo(receiver);
            var typeStr = typeInfo.Type?.ToDisplayString();
            if (typeStr != null && typeStr.Contains("RoutingSlipBuilder"))
            {
                builderType = typeStr;
                builderKey = GetReceiverSymbolKey(receiver);
                return true;
            }
        }

        // 2. Fallback sintáctico: receptor que es una variable llamada *builder* o
        //    una construcción/encadenamiento de RoutingSlipBuilder.
        if (receiver is ObjectCreationExpressionSyntax oc && GetSimpleTypeName(oc.Type).Contains("RoutingSlipBuilder"))
        {
            builderKey = null; // encadenado
            return true;
        }
        if (receiver is IdentifierNameSyntax id && id.Identifier.Text.IndexOf("builder", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            builderKey = id.Identifier.Text;
            return true;
        }
        // Encadenamiento builder.AddActivity(..).AddActivity(..)
        if (receiver is InvocationExpressionSyntax innerInv && IsRoutingSlipBuilderCall(innerInv, out var innerKey, out _))
        {
            builderKey = innerKey;
            return true;
        }

        return false;
    }

    private string? GetReceiverSymbolKey(ExpressionSyntax receiver)
    {
        if (_semanticModel == null) return null;
        var symbol = _semanticModel.GetSymbolInfo(receiver).Symbol;
        return symbol?.ToDisplayString();
    }

    private RoutingSlipStep? ExtractStep(InvocationExpressionSyntax inv, MemberAccessExpressionSyntax member)
    {
        string? name = null;
        string? argsType = null;

        // Nombre de la actividad: primer argumento string literal.
        foreach (var arg in inv.ArgumentList.Arguments)
        {
            if (arg.Expression is LiteralExpressionSyntax lit && lit.Token.Value is string sName)
            {
                name = sName;
                break;
            }
        }

        // Tipo de argumentos: 2º parámetro genérico AddActivity<TActivity,TArguments>
        if (member.Name is GenericNameSyntax gen && gen.TypeArgumentList.Arguments.Count >= 2)
        {
            argsType = GetTypeDisplayString(gen.TypeArgumentList.Arguments[1]);
        }
        else
        {
            // o el primer argumento cuyo tipo sea una clase con nombre (el objeto de argumentos).
            foreach (var arg in inv.ArgumentList.Arguments)
            {
                var t = _semanticModel?.GetTypeInfo(arg.Expression).Type;
                if (t == null || t.Kind == SymbolKind.ErrorType) continue;
                var display = t.ToDisplayString();
                if (t.IsAnonymousType) continue;
                if (display is "string" or "System.String" || display.EndsWith("Uri") || display.EndsWith("Guid")) continue;
                if (t.TypeKind == TypeKind.Class || t.TypeKind == TypeKind.Struct)
                {
                    argsType = display;
                    break;
                }
            }
        }

        if (name == null && argsType == null) return null;
        return new RoutingSlipStep(name ?? argsType!, argsType);
    }

    private string? ExtractSubscriptionEvent(InvocationExpressionSyntax inv)
    {
        // AddSubscription(address, RoutingSlipEvents.Completed, ...) -> "Completed"
        foreach (var arg in inv.ArgumentList.Arguments)
        {
            if (arg.Expression is MemberAccessExpressionSyntax ma &&
                ma.Expression.ToString().Contains("RoutingSlipEvents"))
            {
                return ma.Name.Identifier.Text;
            }
        }
        return null;
    }

    private bool IsRoutingSlipType(ExpressionSyntax expr)
    {
        if (_semanticModel != null)
        {
            var t = _semanticModel.GetTypeInfo(expr).Type;
            if (t != null && t.Kind != SymbolKind.ErrorType)
            {
                var d = t.ToDisplayString();
                return d.EndsWith(".RoutingSlip") || d == "RoutingSlip";
            }
        }
        // Fallback: nombre de variable que sugiere una routing slip.
        var s = expr.ToString();
        return s.IndexOf("routingSlip", StringComparison.OrdinalIgnoreCase) >= 0
               || s.IndexOf("routingslip", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // Nombres EXACTOS de las interfaces reales de cada librería (sin sufijos amplios:
    // un wrapper propio como IEventPublisher/ICommandSender NO debe contar como MediatR).
    private static readonly HashSet<string> MediatRTypeNames = new()
        { "IMediator", "ISender", "IPublisher" };
    private static readonly HashSet<string> MassTransitTypeNames = new()
        { "IBus", "IBusControl", "IPublishEndpoint", "ISendEndpoint", "ISendEndpointProvider",
          "IRequestClient", "ConsumeContext", "ConsumeContext`1" };

    private string DetermineProvider(ExpressionSyntax receiverExpression)
    {
        if (_semanticModel != null)
        {
            var type = _semanticModel.GetTypeInfo(receiverExpression).Type;
            if (type != null && type.Kind != SymbolKind.ErrorType)
            {
                var provider = ProviderFromType(type);
                if (provider != null) return provider;

                // El receptor es un tipo propio (p. ej. un wrapper IEventPublisher).
                // Miramos las interfaces que implementa por si envuelven a una de las
                // librerías conocidas.
                foreach (var iface in type.AllInterfaces)
                {
                    var p = ProviderFromType(iface);
                    if (p != null) return p;
                }

                // Wrapper sin pistas: por defecto MassTransit (bus entre servicios).
                return "MassTransit";
            }
        }

        // Fallback puramente sintáctico (sin semántica disponible): solo marcamos
        // MediatR ante señales inequívocas. "publisher"/"sender" son demasiado
        // ambiguos (los wrappers de MassTransit suelen llamarse así), así que NO se usan.
        var exprStr = receiverExpression.ToString().ToLowerInvariant();
        if (exprStr.Contains("mediatr") || exprStr.Contains("mediator"))
        {
            return "MediatR";
        }
        return "MassTransit"; // Predeterminado
    }

    /// <summary>Devuelve "MediatR"/"MassTransit" según el namespace real del tipo, o por
    /// el nombre exacto de la interfaz (para stubs/casos sin namespace de librería); null si no se sabe.</summary>
    private static string? ProviderFromType(ITypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
        if (NamespaceMatches(ns, "MediatR")) return "MediatR";
        if (NamespaceMatches(ns, "MassTransit")) return "MassTransit";

        var name = type.Name;
        if (MediatRTypeNames.Contains(name)) return "MediatR";
        if (MassTransitTypeNames.Contains(name)) return "MassTransit";
        return null;
    }

    private static bool NamespaceMatches(string ns, string root)
    {
        return ns == root || ns.StartsWith(root + ".", StringComparison.Ordinal);
    }

    private string? GetCallTypeFromMethodName(string methodName)
    {
        return methodName switch
        {
            "Publish" or "PublishAsync" => "Publish",
            "Send" or "SendAsync" => "Send",
            "Request" or "RequestAsync" or "GetResponse" or "GetResponseAsync" => "Request",
            "Respond" or "RespondAsync" => "Respond",
            _ => null
        };
    }

    private GenericNameSyntax? GetGenericName(TypeSyntax typeSyntax)
    {
        if (typeSyntax is GenericNameSyntax gns) return gns;
        if (typeSyntax is QualifiedNameSyntax qns)
        {
            return GetGenericName(qns.Right);
        }
        return null;
    }

    private string GetLocationDescription(SyntaxNode node)
    {
        var span = node.SyntaxTree.GetLineSpan(node.Span);
        var fileName = Path.GetFileName(node.SyntaxTree.FilePath);
        var line = span.StartLinePosition.Line + 1;

        var enclosingMethod = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        var enclosingClass = node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();

        var desc = $"{fileName}:L{line}";
        if (enclosingClass != null)
        {
            var className = enclosingClass.Identifier.Text;
            if (enclosingMethod != null)
            {
                desc += $" ({className}.{enclosingMethod.Identifier.Text})";
            }
            else
            {
                desc += $" ({className})";
            }
        }
        return desc;
    }

    private string GetProjectName(SyntaxNode node)
    {
        // Cuando el análisis viene de MSBuild conocemos el nombre real del proyecto
        // Roslyn, mucho más fiable que inferirlo por la ruta del archivo.
        if (!string.IsNullOrEmpty(_projectNameOverride)) return _projectNameOverride;

        if (string.IsNullOrEmpty(_scanRoot)) return "Unknown";
        
        var filePath = node.SyntaxTree.FilePath;
        if (string.IsNullOrEmpty(filePath)) return "Unknown";

        try
        {
            // 1. Intentar encontrar un .csproj subiendo por el árbol de directorios
            var dir = Path.GetDirectoryName(filePath);
            var scanRootNormalized = Path.GetFullPath(_scanRoot);
            
            while (dir != null)
            {
                var csprojFiles = Directory.GetFiles(dir, "*.csproj");
                if (csprojFiles.Length > 0)
                {
                    return Path.GetFileNameWithoutExtension(csprojFiles[0]);
                }
                
                // Si llegamos al directorio raíz del escaneo, no seguimos subiendo para evitar encontrar .csproj externos
                if (Path.GetFullPath(dir).Equals(scanRootNormalized, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                
                dir = Path.GetDirectoryName(dir);
            }

            // 2. Fallback: Usar la carpeta relativa de primer nivel dentro de la raíz de escaneo o el directorio contenedor directo
            var relativePath = Path.GetRelativePath(scanRootNormalized, filePath);
            var parts = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
            {
                int idx = 0;
                while (idx < parts.Length - 1 && (parts[idx] == "src" || parts[idx] == "Services" || parts[idx] == "services" || parts[idx] == "srcs"))
                {
                    idx++;
                }
                if (idx < parts.Length - 1)
                {
                    return parts[idx];
                }
                return Path.GetFileName(Path.GetDirectoryName(filePath)) ?? "Unknown";
            }
            else
            {
                return Path.GetFileName(scanRootNormalized) ?? "Unknown";
            }
        }
        catch
        {
            try
            {
                return Path.GetFileName(Path.GetDirectoryName(filePath)) ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }
    }

    private string GetCodeSnippet(SyntaxNode node, int contextLinesBefore = 3, int contextLinesAfter = 3)
    {
        try
        {
            var syntaxTree = node.SyntaxTree;
            var sourceText = syntaxTree.GetText();
            var lineSpan = syntaxTree.GetLineSpan(node.Span);
            var startLine = lineSpan.StartLinePosition.Line;
            var endLine = lineSpan.EndLinePosition.Line;

            int fromLine = Math.Max(0, startLine - contextLinesBefore);
            int toLine = Math.Min(sourceText.Lines.Count - 1, endLine + contextLinesAfter);

            if (node is ClassDeclarationSyntax && (toLine - fromLine > 25))
            {
                toLine = Math.Min(sourceText.Lines.Count - 1, fromLine + 25);
            }

            var lines = new List<string>();
            for (int i = fromLine; i <= toLine; i++)
            {
                lines.Add(sourceText.Lines[i].ToString());
            }

            return string.Join("\n", lines);
        }
        catch
        {
            return node.ToString();
        }
    }
}
