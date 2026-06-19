using System;
using System.Collections.Generic;

namespace MessageFlowExplorer.Core;

public record MessageInfo(string Type, string Category);

public record ProducerInfo(string Location, string MessageType, string CallType, string Project = "Unknown", string? CodeSnippet = null, string Provider = "MassTransit", string? ResponseType = null);

// Kind: clasifica la intención del consumidor para inferir la categoría del mensaje.
//   "Consumer"            -> MassTransit IConsumer (intención según el verbo de envío)
//   "NotificationHandler" -> MediatR INotificationHandler (evento)
//   "RequestHandler"      -> MediatR IRequestHandler con respuesta real (query/request)
//   "CommandHandler"      -> MediatR IRequestHandler sin respuesta útil (Unit) (comando)
public record ConsumerInfo(string Location, string MessageType, string ConsumerType, string Project = "Unknown", string? CodeSnippet = null, string Provider = "MassTransit", string Kind = "Consumer");

public record SagaInfo(string Location, string SagaType, List<string> ConsumedEvents, List<string> PublishedMessages, string Project = "Unknown", string? CodeSnippet = null);

public record ActivityInfo(string Location, string ActivityType, string ArgumentsType, string? CompensateLogType, string Project = "Unknown", string? CodeSnippet = null);

/// <summary>Un paso del itinerario de una routing slip: la actividad y el tipo de sus argumentos.</summary>
public record RoutingSlipStep(string Name, string? ArgumentsType);

/// <summary>
/// Una routing slip detectada (construida con RoutingSlipBuilder). Captura el itinerario
/// ordenado, los eventos del ciclo de vida a los que se suscribe (Completed/Faulted...) y
/// si se llega a ejecutar, para poder detectar slips que "no terminan" o sin manejo de fallo.
/// </summary>
public record RoutingSlipInfo(
    string Location,
    List<RoutingSlipStep> Itinerary,
    List<string> SubscribedEvents,
    bool IsExecuted,
    string Project = "Unknown",
    string? CodeSnippet = null);

public record TopologyReport(
    List<MessageInfo> Messages,
    List<ProducerInfo> Producers,
    List<ConsumerInfo> Consumers,
    List<SagaInfo> Sagas,
    List<ActivityInfo> Activities,
    List<RoutingSlipInfo> RoutingSlips,
    DateTime ScanTime);
