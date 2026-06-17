import React from 'react';
import { Handle, Position } from '@xyflow/react';
import { Send, Inbox, Terminal, Zap, HelpCircle, Cpu, Workflow, AlertTriangle, Route } from 'lucide-react';

const getShortName = (fullName) => {
  if (!fullName) return '';
  const parts = fullName.split('.');
  return parts[parts.length - 1];
};

export const ProducerNode = ({ data, selected }) => {
  const shortClass = getShortName(data.className || data.location);
  const isMediatr = data.provider === 'MediatR';
  const nodeClass = isMediatr ? 'node-mediatr-producer' : 'node-producer';
  const glowClass = isMediatr ? 'glow-mediatr' : 'glow-producer';
  const iconClass = isMediatr ? 'icon-mediatr' : 'icon-producer';
  
  return (
    <div 
      className={`node-card ${nodeClass} ${selected ? `node-card-selected ${glowClass}` : ''}`}
    >
      <div className="node-header">
        <div className={`node-icon-container ${iconClass}`}>
          <Send size={14} />
        </div>
        <span className={`node-label ${isMediatr ? 'text-mediatr' : 'text-producer'}`}>Publisher</span>
        {isMediatr && (
          <span className="provider-badge provider-badge-mediatr">MediatR</span>
        )}
        {data.project && data.project !== 'Unknown' && !isMediatr && (
          <span className="node-project-badge" title={`Microservice: ${data.project}`}>{data.project}</span>
        )}
      </div>
      
      <div className="node-title" title={data.className}>
        {data.methodName ? `${shortClass}.${data.methodName}` : shortClass}
      </div>
      
      <div className="node-subtitle" title={data.location}>
        {data.location.split(' ')[0]}
      </div>

      <Handle
        type="source"
        position={Position.Right}
        className="node-handle handle-producer"
      />
    </div>
  );
};

export const MessageNode = ({ data, selected }) => {
  const shortName = getShortName(data.type);
  const category = data.category || 'Event';
  
  let categoryClass = 'msg-event';
  let Icon = Zap;
  
  if (category === 'Command') {
    categoryClass = 'msg-command';
    Icon = Terminal;
  } else if (category === 'Request') {
    categoryClass = 'msg-request';
    Icon = HelpCircle;
  }

  const isOrphan = data.hasProducer === false || data.hasConsumer === false;
  const warningClass = isOrphan ? 'node-message-warning' : '';
  
  const tooltipParts = [];
  if (data.hasProducer === false) tooltipParts.push('sin publicador');
  if (data.hasConsumer === false) tooltipParts.push('sin consumidor');
  const tooltipText = isOrphan ? `Advertencia: Mensaje ${tooltipParts.join(' y ')}.` : '';

  return (
    <div 
      className={`node-card node-message ${categoryClass} ${warningClass} ${selected ? `node-card-selected glow-message` : ''}`}
      title={tooltipText || data.type}
    >
      <Handle
        type="target"
        position={Position.Left}
        id="input"
        className="node-handle handle-message"
      />
      
      <div className="node-header">
        <div className="node-icon-container">
          <Icon size={14} />
        </div>
        <span className="node-label">{category}</span>
        {isOrphan && (
          <div className="node-warning-icon" title={tooltipText}>
            <AlertTriangle size={12} />
          </div>
        )}
      </div>
      
      <div className="node-title" title={data.type}>
        {shortName}
      </div>
      
      <div className="node-subtitle" title={data.type}>
        {isOrphan ? (
          <span className="text-warning-text font-bold">
            {data.hasProducer === false && data.hasConsumer === false
              ? 'Sin Publicador/Consumidor'
              : data.hasProducer === false
              ? 'Sin Publicador'
              : 'Sin Consumidor'}
          </span>
        ) : (
          data.type.substring(0, data.type.lastIndexOf('.')) || 'Contracts'
        )}
      </div>

      <Handle
        type="source"
        position={Position.Right}
        id="output"
        className="node-handle handle-message"
      />
    </div>
  );
};

export const ConsumerNode = ({ data, selected }) => {
  const shortClass = getShortName(data.consumerType);
  const isMediatr = data.provider === 'MediatR';
  const nodeClass = isMediatr ? 'node-mediatr-consumer' : 'node-consumer';
  const glowClass = isMediatr ? 'glow-mediatr' : 'glow-consumer';
  const iconClass = isMediatr ? 'icon-mediatr' : 'icon-consumer';
  const label = isMediatr ? 'Handler' : 'Consumer';
  
  return (
    <div 
      className={`node-card ${nodeClass} ${selected ? `node-card-selected ${glowClass}` : ''}`}
    >
      <Handle
        type="target"
        position={Position.Left}
        className="node-handle handle-consumer"
      />
      
      <div className="node-header">
        <div className={`node-icon-container ${iconClass}`}>
          <Inbox size={14} />
        </div>
        <span className={`node-label ${isMediatr ? 'text-mediatr' : 'text-consumer'}`}>{label}</span>
        {isMediatr && (
          <span className="provider-badge provider-badge-mediatr">MediatR</span>
        )}
        {data.project && data.project !== 'Unknown' && !isMediatr && (
          <span className="node-project-badge" title={`Microservice: ${data.project}`}>{data.project}</span>
        )}
      </div>
      
      <div className="node-title" title={data.consumerType}>
        {shortClass}
      </div>
      
      <div className="node-subtitle" title={data.location}>
        {data.location}
      </div>
    </div>
  );
};

export const SagaNode = ({ data, selected }) => {
  const shortClass = getShortName(data.sagaType);
  
  return (
    <div 
      className={`node-card node-saga ${selected ? 'node-card-selected glow-saga' : ''}`}
    >
      <Handle
        type="target"
        position={Position.Left}
        id="input"
        className="node-handle handle-saga"
      />
      
      <div className="node-header">
        <div className="node-icon-container icon-saga">
          <Cpu size={14} />
        </div>
        <span className="node-label text-saga">Saga</span>
        {data.project && data.project !== 'Unknown' && (
          <span className="node-project-badge" title={`Microservice: ${data.project}`}>{data.project}</span>
        )}
      </div>
      
      <div className="node-title" title={data.sagaType}>
        {shortClass}
      </div>
      
      <div className="node-subtitle" title={data.location}>
        {data.location}
      </div>

      <Handle
        type="source"
        position={Position.Right}
        id="output"
        className="node-handle handle-saga"
      />
    </div>
  );
};

export const ActivityNode = ({ data, selected }) => {
  const shortClass = getShortName(data.activityType);
  
  return (
    <div 
      className={`node-card node-activity ${selected ? 'node-card-selected glow-activity' : ''}`}
    >
      <Handle
        type="target"
        position={Position.Left}
        id="input"
        className="node-handle handle-activity"
      />
      
      <div className="node-header">
        <div className="node-icon-container icon-activity">
          <Workflow size={14} />
        </div>
        <span className="node-label text-activity">Activity</span>
        {data.project && data.project !== 'Unknown' && (
          <span className="node-project-badge" title={`Microservice: ${data.project}`}>{data.project}</span>
        )}
      </div>
      
      <div className="node-title" title={data.activityType}>
        {shortClass}
      </div>
      
      <div className="node-subtitle" title={data.location}>
        {data.location}
      </div>

      <Handle
        type="source"
        position={Position.Right}
        id="output"
        className="node-handle handle-activity"
      />
    </div>
  );
};

export const RoutingSlipNode = ({ data, selected }) => {
  const itinerary = data.itinerary || [];
  const events = data.subscribedEvents || [];
  const hasCompleted = events.some(e => e.includes('Completed'));
  const hasFaulted = events.some(e => e.includes('Faulted'));
  const warning = !hasFaulted; // fallo no manejado: la advertencia clave

  return (
    <div
      className={`node-card node-routingslip ${warning ? 'node-rs-warning' : ''} ${selected ? 'node-card-selected glow-routingslip' : ''}`}
    >
      <Handle
        type="target"
        position={Position.Left}
        id="input"
        className="node-handle handle-routingslip"
      />

      <div className="node-header">
        <div className="node-icon-container icon-routingslip">
          <Route size={14} />
        </div>
        <span className="node-label text-routingslip">Routing Slip</span>
        {warning && (
          <div className="node-warning-icon" title="Sin suscripción a 'Faulted': los fallos podrían no manejarse">
            <AlertTriangle size={12} />
          </div>
        )}
        {data.project && data.project !== 'Unknown' && (
          <span className="node-project-badge" title={`Microservice: ${data.project}`}>{data.project}</span>
        )}
      </div>

      <div className="node-title" title={data.location}>
        {itinerary.length} actividad{itinerary.length === 1 ? '' : 'es'}
      </div>

      <div className="rs-itinerary">
        {itinerary.map((step, idx) => (
          <div className="rs-step" key={idx}>
            <span className="rs-step-index">{idx + 1}</span>
            <span className="rs-step-name" title={step.argumentsType || step.name}>{step.name}</span>
            {idx < itinerary.length - 1 && <span className="rs-step-arrow">↓</span>}
          </div>
        ))}
      </div>

      <div className="rs-events">
        <span className={`rs-event-badge ${hasCompleted ? 'rs-event-ok' : 'rs-event-missing'}`}>
          {hasCompleted ? '✓ Completed' : '⚠ Sin Completed'}
        </span>
        <span className={`rs-event-badge ${hasFaulted ? 'rs-event-ok' : 'rs-event-missing'}`}>
          {hasFaulted ? '✓ Faulted' : '⚠ Sin Faulted'}
        </span>
      </div>

      {!data.isExecuted && (
        <div className="rs-not-executed" title="No se vio bus.Execute en el mismo método">
          ℹ Ejecución no detectada en el mismo método
        </div>
      )}

      <Handle
        type="source"
        position={Position.Right}
        id="output"
        className="node-handle handle-routingslip"
      />
    </div>
  );
};

export const GroupNode = ({ data }) => {
  return (
    <div className="group-node-container">
      <div className="group-node-label">{data.label}</div>
    </div>
  );
};
