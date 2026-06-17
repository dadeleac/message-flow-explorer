import React, { useState } from 'react';
import { X, Code, FileText, ArrowRightLeft, GitMerge, Server, Copy, Check, Share2 } from 'lucide-react';

const getActorName = (locationOrType) => {
  if (!locationOrType) return 'Component';
  
  if (locationOrType.includes(' (')) {
    const classMethod = locationOrType.split(' (')[1]?.replace(')', '');
    if (classMethod) {
      const parts = classMethod.split('.');
      if (parts.length > 1) {
        return parts[0].replace(/[^a-zA-Z0-9_]/g, '');
      }
      return classMethod.replace(/[^a-zA-Z0-9_]/g, '');
    }
  }

  if (locationOrType.includes('.cs:')) {
    return locationOrType.split('.cs:')[0].replace(/[^a-zA-Z0-9_]/g, '');
  }

  const lastPart = locationOrType.split('.').pop();
  return lastPart.replace(/[^a-zA-Z0-9_]/g, '');
};

const generateMermaidSequence = (node, allData) => {
  if (!node || !allData) return '';

  const { data } = node;
  const isProducer = node.type === 'producerNode';
  const isMessage = node.type === 'messageNode';
  const isConsumer = node.type === 'consumerNode';
  const isSaga = node.type === 'sagaNode';
  const isActivity = node.type === 'activityNode';
  const isRoutingSlip = node.type === 'routingSlipNode';

  if (isRoutingSlip) {
    const itinerary = data.itinerary || [];
    const events = data.subscribedEvents || [];
    const flow = ['flowchart LR', '    Start([Inicio])'];
    let prev = 'Start';
    itinerary.forEach((step, idx) => {
      const id = `A${idx}`;
      flow.push(`    ${id}["${step.name}"]`);
      flow.push(`    ${prev} --> ${id}`);
      prev = id;
    });
    if (events.some(e => e.includes('Completed'))) {
      flow.push('    Completed([Completed])');
      flow.push(`    ${prev} --> Completed`);
    } else {
      flow.push('    EndNote([Sin Completed])');
      flow.push(`    ${prev} --> EndNote`);
    }
    if (events.some(e => e.includes('Faulted'))) {
      flow.push('    Faulted([Faulted / Compensación])');
      itinerary.forEach((_, idx) => flow.push(`    A${idx} -.fallo.-> Faulted`));
    } else {
      flow.push('    %% Aviso: sin suscripción a Faulted (fallos no manejados)');
    }
    return flow.join('\n');
  }

  let lines = [];
  lines.push('sequenceDiagram');
  lines.push('    autonumber');
  lines.push('');

  const cleanMsg = (type) => type.split('.').pop();

  if (isMessage) {
    const msgType = data.type;
    const msgShort = cleanMsg(msgType);

    const prods = allData.producers.filter(p => p.messageType === msgType);
    const sagasPub = (allData.sagas || []).filter(s => s.publishedMessages.includes(msgType));
    
    const cons = allData.consumers.filter(c => c.messageType === msgType);
    const sagasSub = (allData.sagas || []).filter(s => s.consumedEvents.includes(msgType));
    const acts = (allData.activities || []).filter(a => a.argumentsType === msgType);

    const sources = [
      ...prods.map(p => getActorName(p.location)),
      ...sagasPub.map(s => getActorName(s.sagaType))
    ];
    const uniqueSources = Array.from(new Set(sources));

    const targets = [
      ...cons.map(c => getActorName(c.consumerType)),
      ...sagasSub.map(s => getActorName(s.sagaType)),
      ...acts.map(a => getActorName(a.activityType))
    ];
    const uniqueTargets = Array.from(new Set(targets));

    if (uniqueSources.length > 0 && uniqueTargets.length > 0) {
      uniqueSources.forEach(src => {
        uniqueTargets.forEach(tgt => {
          lines.push(`    ${src}->>${tgt}: ${msgShort}`);
        });
      });
    } else if (uniqueSources.length > 0) {
      uniqueSources.forEach(src => {
        lines.push(`    ${src}->>MessageBus: ${msgShort} (Publicado)`);
      });
    } else if (uniqueTargets.length > 0) {
      uniqueTargets.forEach(tgt => {
        lines.push(`    MessageBus->>${tgt}: ${msgShort} (Consumido)`);
      });
    } else {
      lines.push(`    %% Mensaje huerfano: ${msgShort}`);
    }
  }
  else if (isProducer) {
    const prodActor = getActorName(data.className || data.location);
    const prods = allData.producers.filter(p => p.location === data.location);
    
    prods.forEach(p => {
      const msgShort = cleanMsg(p.messageType);
      const cons = allData.consumers.filter(c => c.messageType === p.messageType);
      const sagas = (allData.sagas || []).filter(s => s.consumedEvents.includes(p.messageType));
      const acts = (allData.activities || []).filter(a => a.argumentsType === p.messageType);

      const targets = [
        ...cons.map(c => getActorName(c.consumerType)),
        ...sagas.map(s => getActorName(s.sagaType)),
        ...acts.map(a => getActorName(a.activityType))
      ];
      const uniqueTargets = Array.from(new Set(targets));

      if (uniqueTargets.length > 0) {
        uniqueTargets.forEach(tgt => {
          lines.push(`    ${prodActor}->>${tgt}: ${msgShort}`);
        });
      } else {
        lines.push(`    ${prodActor}->>MessageBus: ${msgShort}`);
      }
    });
  }
  else if (isConsumer) {
    const consActor = getActorName(data.consumerType);
    
    const consumedMsgs = allData.consumers.filter(c => c.consumerType === data.consumerType);
    consumedMsgs.forEach(c => {
      const msgShort = cleanMsg(c.messageType);
      const prods = allData.producers.filter(p => p.messageType === c.messageType);
      const sagas = (allData.sagas || []).filter(s => s.publishedMessages.includes(c.messageType));
      
      const sources = [
        ...prods.map(p => getActorName(p.location)),
        ...sagas.map(s => getActorName(s.sagaType))
      ];
      const uniqueSources = Array.from(new Set(sources));

      if (uniqueSources.length > 0) {
        uniqueSources.forEach(src => {
          lines.push(`    ${src}->>${consActor}: ${msgShort}`);
        });
      } else {
        lines.push(`    MessageBus->>${consActor}: ${msgShort}`);
      }
    });

    const consumerShortName = data.consumerType.split('.').pop();
    const internalProds = allData.producers.filter(p => p.location.includes(consumerShortName));
    internalProds.forEach(p => {
      const msgShort = cleanMsg(p.messageType);
      const cons = allData.consumers.filter(c => c.messageType === p.messageType);
      const sagas = (allData.sagas || []).filter(s => s.consumedEvents.includes(p.messageType));
      
      const targets = [
        ...cons.map(c => getActorName(c.consumerType)),
        ...sagas.map(s => getActorName(s.sagaType))
      ];
      const uniqueTargets = Array.from(new Set(targets));

      if (uniqueTargets.length > 0) {
        uniqueTargets.forEach(tgt => {
          lines.push(`    ${consActor}->>${tgt}: ${msgShort}`);
        });
      } else {
        lines.push(`    ${consActor}->>MessageBus: ${msgShort}`);
      }
    });
  }
  else if (isSaga) {
    const sagaActor = getActorName(data.sagaType);
    const sagaDef = (allData.sagas || []).find(s => s.sagaType === data.sagaType);
    
    if (sagaDef) {
      sagaDef.consumedEvents.forEach(ev => {
        const msgShort = cleanMsg(ev);
        const prods = allData.producers.filter(p => p.messageType === ev);
        const sagas = (allData.sagas || []).filter(s => s.publishedMessages.includes(ev));
        
        const sources = [
          ...prods.map(p => getActorName(p.location)),
          ...sagas.map(s => getActorName(s.sagaType))
        ];
        const uniqueSources = Array.from(new Set(sources));

        if (uniqueSources.length > 0) {
          uniqueSources.forEach(src => {
            lines.push(`    ${src}->>${sagaActor}: ${msgShort}`);
          });
        } else {
          lines.push(`    MessageBus->>${sagaActor}: ${msgShort}`);
        }
      });

      sagaDef.publishedMessages.forEach(pub => {
        const msgShort = cleanMsg(pub);
        const cons = allData.consumers.filter(c => c.messageType === pub);
        const sagas = (allData.sagas || []).filter(s => s.consumedEvents.includes(pub));
        
        const targets = [
          ...cons.map(c => getActorName(c.consumerType)),
          ...sagas.map(s => getActorName(s.sagaType))
        ];
        const uniqueTargets = Array.from(new Set(targets));

        if (uniqueTargets.length > 0) {
          uniqueTargets.forEach(tgt => {
            lines.push(`    ${sagaActor}->>${tgt}: ${msgShort}`);
          });
        } else {
          lines.push(`    ${sagaActor}->>MessageBus: ${msgShort}`);
        }
      });
    }
  }
  else if (isActivity) {
    const actActor = getActorName(data.activityType);
    const activityDef = (allData.activities || []).find(a => a.activityType === data.activityType);
    
    if (activityDef) {
      const msgShort = cleanMsg(activityDef.argumentsType);
      const prods = allData.producers.filter(p => p.messageType === activityDef.argumentsType);
      const sagas = (allData.sagas || []).filter(s => s.publishedMessages.includes(activityDef.argumentsType));
      
      const sources = [
        ...prods.map(p => getActorName(p.location)),
        ...sagas.map(s => getActorName(s.sagaType))
      ];
      const uniqueSources = Array.from(new Set(sources));

      if (uniqueSources.length > 0) {
        uniqueSources.forEach(src => {
          lines.push(`    ${src}->>${actActor}: ${msgShort}`);
        });
      } else {
        lines.push(`    MessageBus->>${actActor}: ${msgShort}`);
      }

      if (activityDef.compensateLogType) {
        const logShort = cleanMsg(activityDef.compensateLogType);
        lines.push(`    ${actActor}->>Compensator: ${logShort} (Compensate Log)`);
      }
    }
  }

  return lines.join('\n');
};

const dedentCode = (code) => {
  if (!code) return '';
  const lines = code.split('\n');
  let minIndent = null;
  
  lines.forEach(line => {
    if (line.trim().length === 0) return;
    const match = line.match(/^(\s*)/);
    const indent = match ? match[1].length : 0;
    if (minIndent === null || indent < minIndent) {
      minIndent = indent;
    }
  });

  if (minIndent === null || minIndent === 0) return code;

  return lines.map(line => {
    if (line.trim().length === 0) return '';
    return line.slice(minIndent);
  }).join('\n');
};

const highlightCSharp = (code) => {
  if (!code) return '';
  
  let escaped = code
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');

  const tokenRegex = /(\/\/.*)|(@?"[^"\\]*(?:\\.[^"\\]*)*")|\b(public|private|protected|internal|class|record|interface|void|string|int|var|await|async|return|new|using|namespace|get|set|List|Task|ISaga|IConsumer|IActivity|Event|null|Guid|DateTime|override|bool|decimal)\b|\b(Publish|PublishAsync|Send|SendAsync|Respond|RespondAsync|Request|RequestAsync|Consume|GetResponse|GetResponseAsync)\b/g;

  return escaped.replace(tokenRegex, (match, comment, str, kw, method) => {
    if (comment) return `<span class="code-comment">${comment}</span>`;
    if (str) return `<span class="code-str">${str}</span>`;
    if (kw) return `<span class="code-kw">${kw}</span>`;
    if (method) return `<span class="code-method">${method}</span>`;
    return match;
  });
};

export default function DetailsPanel({ node, onClose, allData }) {
  const [copied, setCopied] = useState(false);
  const [showCode, setShowCode] = useState(true);

  if (!node) return null;

  const { data } = node;
  const isProducer = node.type === 'producerNode';
  const isMessage = node.type === 'messageNode';
  const isConsumer = node.type === 'consumerNode';
  const isSaga = node.type === 'sagaNode';
  const isActivity = node.type === 'activityNode';
  const isRoutingSlip = node.type === 'routingSlipNode';

  let title = 'Detalles';
  let badgeClass = 'badge-default';
  let badgeText = 'Desconocido';

  if (isRoutingSlip) {
    title = 'Routing Slip';
    badgeClass = 'badge-routingslip';
    badgeText = 'Routing Slip';
  } else if (isProducer) {
    title = 'Publicador';
    badgeClass = data.provider === 'MediatR' ? 'badge-mediatr' : 'badge-producer';
    badgeText = data.provider === 'MediatR' ? 'MediatR Publisher' : 'Publisher';
  } else if (isConsumer) {
    title = data.provider === 'MediatR' ? 'Handler (MediatR)' : 'Consumidor';
    badgeClass = data.provider === 'MediatR' ? 'badge-mediatr' : 'badge-consumer';
    badgeText = data.provider === 'MediatR' ? 'MediatR Handler' : 'Consumer';
  } else if (isSaga) {
    title = 'Saga (Orquestador)';
    badgeClass = 'badge-saga';
    badgeText = 'Saga';
  } else if (isActivity) {
    title = 'Actividad';
    badgeClass = 'badge-activity';
    badgeText = 'Activity';
  } else if (isMessage) {
    title = 'Contrato de Mensaje';
    const category = data.category || 'Event';
    if (category === 'Command') {
      badgeClass = 'badge-command';
      badgeText = 'Command';
    } else if (category === 'Request') {
      badgeClass = 'badge-request';
      badgeText = 'Request';
    } else {
      badgeClass = 'badge-event';
      badgeText = 'Event';
    }
  }

  // Encontrar conexiones asociadas
  const getConnections = () => {
    if (!allData) return { inputs: [], outputs: [] };

    if (isMessage) {
      // Inputs: productores o sagas que publican este mensaje
      const inputs = [
        ...allData.producers
          .filter(p => p.messageType === data.type)
          .map(p => ({
            label: p.methodName ? `${p.className}.${p.methodName}` : p.location.split(' ')[0],
            type: 'Publisher'
          })),
        ...(allData.sagas || [])
          .filter(s => s.publishedMessages.includes(data.type))
          .map(s => ({
            label: s.sagaType.split('.').pop(),
            type: 'Saga'
          }))
      ];
      
      // Outputs: consumidores, sagas o actividades que consumen este mensaje
      const outputs = [
        ...allData.consumers
          .filter(c => c.messageType === data.type)
          .map(c => ({
            label: c.consumerType.split('.').pop(),
            type: 'Consumer'
          })),
        ...(allData.sagas || [])
          .filter(s => s.consumedEvents.includes(data.type))
          .map(s => ({
            label: s.sagaType.split('.').pop(),
            type: 'Saga'
          })),
        ...(allData.activities || [])
          .filter(a => a.argumentsType === data.type)
          .map(a => ({
            label: a.activityType.split('.').pop(),
            type: 'Activity'
          }))
      ];

      return { inputs, outputs };
    }

    if (isProducer) {
      // Outputs: mensajes que publica este productor
      const outputs = allData.producers
        .filter(p => p.location === data.location && p.messageType === data.messageType)
        .map(p => ({
          label: p.messageType.split('.').pop(),
          type: 'Message'
        }));
      return { inputs: [], outputs };
    }

    if (isConsumer) {
      // Inputs: mensaje que consume
      const inputs = allData.consumers
        .filter(c => c.consumerType === data.consumerType && c.messageType === data.messageType)
        .map(c => ({
          label: c.messageType.split('.').pop(),
          type: 'Message'
        }));

      // Outputs: mensajes que publica internamente
      const consumerShortName = data.consumerType.split('.').pop();
      const outputs = allData.producers
        .filter(p => p.location.includes(consumerShortName))
        .map(p => ({
          label: p.messageType.split('.').pop(),
          type: 'Message'
        }));

      return { inputs, outputs };
    }

    if (isSaga) {
      const sagaDef = (allData.sagas || []).find(s => s.sagaType === data.sagaType);
      if (sagaDef) {
        const inputs = sagaDef.consumedEvents.map(ev => ({
          label: ev.split('.').pop(),
          type: 'Message'
        }));
        const outputs = sagaDef.publishedMessages.map(pub => ({
          label: pub.split('.').pop(),
          type: 'Message'
        }));
        return { inputs, outputs };
      }
    }

    if (isActivity) {
      const activityDef = (allData.activities || []).find(a => a.activityType === data.activityType);
      if (activityDef) {
        const inputs = [{
          label: activityDef.argumentsType.split('.').pop(),
          type: 'Message'
        }];
        const outputs = activityDef.compensateLogType ? [{
          label: activityDef.compensateLogType.split('.').pop(),
          type: 'Message'
        }] : [];
        return { inputs, outputs };
      }
    }

    if (isRoutingSlip) {
      // Las "salidas" son las actividades que ejecuta el itinerario, en orden.
      const outputs = (data.itinerary || []).map(step => ({
        label: step.name,
        type: 'Activity'
      }));
      return { inputs: [], outputs };
    }

    return { inputs: [], outputs: [] };
  };

  const { inputs, outputs } = getConnections();

  const handleCopyMermaid = () => {
    const code = generateMermaidSequence(node, allData);
    navigator.clipboard.writeText(code).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  };

  return (
    <div className="details-panel glass-panel-heavy">
      {/* Header */}
      <div className="details-header" style={{ display: 'flex', flexDirection: 'column', alignItems: 'stretch', gap: '8px', borderBottom: '1px solid hsla(var(--border) / 0.5)', paddingBottom: '16px' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', width: '100%' }}>
          <div className="details-header-title-container">
            <span className={`details-badge ${badgeClass}`}>
              {badgeText}
            </span>
            <h2 className="details-title">{title}</h2>
          </div>
          <button onClick={onClose} className="close-button">
            <X size={18} />
          </button>
        </div>
        <button 
          onClick={handleCopyMermaid} 
          className={`btn-mermaid ${copied ? 'btn-mermaid-success' : ''}`}
          style={{ marginTop: '4px' }}
        >
          {copied ? <Check size={14} /> : <Copy size={14} />}
          <span>{copied ? '¡Diagrama Copiado!' : 'Copiar Código Mermaid'}</span>
        </button>
      </div>      {/* Info General */}
      <div className="details-section">
        <div className="details-inspector">
          {/* Nombre completo */}
          <div className="inspector-row">
            <div className="inspector-label">
              <FileText size={12} />
              <span>Identificador / Tipo</span>
            </div>
            <div className="inspector-value font-mono break-word">
              {isProducer && (data.className || data.location)}
              {isMessage && data.type}
              {isConsumer && data.consumerType}
              {isSaga && data.sagaType}
              {isActivity && data.activityType}
              {isRoutingSlip && `Routing Slip · ${(data.itinerary || []).length} actividad(es)`}
            </div>
          </div>

          {/* Proyecto / Microservicio */}
          {data.project && data.project !== 'Unknown' && (
            <div className="inspector-row">
              <div className="inspector-label">
                <Server size={12} />
                <span>Microservicio / Proyecto</span>
              </div>
              <div className="inspector-value text-indigo font-semibold">
                {data.project}
              </div>
            </div>
          )}

          {/* Ubicación */}
          <div className="inspector-row">
            <div className="inspector-label">
              <Code size={12} />
              <span>Ubicación de Código</span>
            </div>
            <div className="inspector-value font-mono break-word text-muted">
              {data.location || 'N/D'}
            </div>
          </div>
        </div>
      </div>

      {/* Itinerario de la Routing Slip */}
      {isRoutingSlip && (() => {
        const itinerary = data.itinerary || [];
        const events = data.subscribedEvents || [];
        const hasCompleted = events.some(e => e.includes('Completed'));
        const hasFaulted = events.some(e => e.includes('Faulted'));
        return (
          <div className="relations-container">
            <div className="relations-title">
              <ArrowRightLeft size={14} />
              <span>Itinerario (orden de ejecución)</span>
            </div>
            <div className="relation-item-list">
              {itinerary.map((step, idx) => (
                <div key={idx} className="relation-item">
                  <span className="rs-step-index">{idx + 1}</span>
                  <div className="relation-item-label" title={step.argumentsType || step.name}>
                    {step.name}
                    {step.argumentsType && (
                      <span className="text-muted font-mono" style={{ marginLeft: 6, fontSize: 10 }}>
                        ({step.argumentsType.split('.').pop()})
                      </span>
                    )}
                  </div>
                </div>
              ))}
              {itinerary.length === 0 && (
                <div className="empty-relations">No se detectaron actividades en el itinerario.</div>
              )}
            </div>

            <div className="rs-events" style={{ marginTop: 12 }}>
              <span className={`rs-event-badge ${hasCompleted ? 'rs-event-ok' : 'rs-event-missing'}`}>
                {hasCompleted ? '✓ Completed' : '⚠ Sin Completed'}
              </span>
              <span className={`rs-event-badge ${hasFaulted ? 'rs-event-ok' : 'rs-event-missing'}`}>
                {hasFaulted ? '✓ Faulted' : '⚠ Sin Faulted'}
              </span>
              <span className={`rs-event-badge ${data.isExecuted ? 'rs-event-ok' : 'rs-event-missing'}`}>
                {data.isExecuted ? '✓ Ejecutada' : 'ℹ Ejecución no vista aquí'}
              </span>
            </div>

            {(!hasFaulted || !hasCompleted) && (
              <div className="rs-not-executed" style={{ marginTop: 10 }}>
                {!hasFaulted && '⚠ Sin suscripción a Faulted: los fallos/compensaciones podrían pasar desapercibidos. '}
                {!hasCompleted && '⚠ Sin suscripción a Completed: no hay confirmación de que la slip finalice.'}
              </div>
            )}

            {data.codeSnippet && (
              <div className="code-preview-container" style={{ borderTop: '1px solid hsla(var(--border) / 0.3)', paddingTop: '12px', marginTop: '12px' }}>
                <div className="code-preview-header" onClick={() => setShowCode(!showCode)}>
                  <Code size={12} />
                  <span>Previsualización C#</span>
                  <span className="code-toggle-btn">{showCode ? 'Ocultar' : 'Mostrar'}</span>
                </div>
                {showCode && (
                  <pre className="code-preview font-mono">
                    <code dangerouslySetInnerHTML={{ __html: highlightCSharp(dedentCode(data.codeSnippet)) }} />
                  </pre>
                )}
              </div>
            )}
          </div>
        );
      })()}

      {/* Relaciones / Flujos */}
      {!isRoutingSlip && (
      <div className="relations-container">
        <div className="relations-title">
          <ArrowRightLeft size={14} />
          <span>Relaciones de Flujo</span>
        </div>

        {/* Entradas (Inputs) */}
        {inputs.length > 0 && (
          <div className="relations-section">
            <span className="relations-section-title">Mensajes Recibidos / Disparadores</span>
            <div className="relation-item-list">
              {inputs.map((input, idx) => (
                <div key={idx} className="relation-item">
                  <GitMerge size={12} className="relation-item-icon rotate-180" />
                  <div className="relation-item-label" title={input.label}>{input.label}</div>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Previsualizador de Código C# */}
        {data.codeSnippet && (
          <div className="code-preview-container" style={{ borderTop: '1px solid hsla(var(--border) / 0.3)', borderBottom: '1px solid hsla(var(--border) / 0.3)', paddingTop: '12px', paddingBottom: '12px', marginTop: '4px', marginBottom: '4px' }}>
            <div 
              className="code-preview-header" 
              onClick={() => setShowCode(!showCode)}
            >
              <Code size={12} />
              <span>Previsualización C#</span>
              <span className="code-toggle-btn">
                {showCode ? 'Ocultar' : 'Mostrar'}
              </span>
            </div>
            {showCode && (
              <pre className="code-preview font-mono">
                <code dangerouslySetInnerHTML={{ __html: highlightCSharp(dedentCode(data.codeSnippet)) }} />
              </pre>
            )}
          </div>
        )}

        {/* Salidas (Outputs) */}
        {outputs.length > 0 && (
          <div className="relations-section">
            <span className="relations-section-title">Mensajes Publicados / Generados</span>
            <div className="relation-item-list">
              {outputs.map((output, idx) => (
                <div key={idx} className="relation-item">
                  <GitMerge size={12} className="relation-item-icon text-indigo" />
                  <div className="relation-item-label" title={output.label}>{output.label}</div>
                </div>
              ))}
            </div>
          </div>
        )}

        {inputs.length === 0 && outputs.length === 0 && (
          <div className="empty-relations">
            Este elemento no tiene relaciones registradas.
          </div>
        )}
      </div>
      )}
    </div>
  );
}
