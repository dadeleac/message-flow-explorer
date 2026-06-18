import React, { useState, useMemo } from 'react';
import {
  ReactFlow,
  MiniMap,
  Controls,
  Background,
  useNodesState,
  useEdgesState,
  MarkerType
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';

import Sidebar from './components/Sidebar';
import DetailsPanel from './components/DetailsPanel';
import { ProducerNode, MessageNode, ConsumerNode, SagaNode, ActivityNode, RoutingSlipNode, GroupNode } from './components/CustomNodes';
import initialData from './sample-output.json';

const nodeTypes = {
  producerNode: ProducerNode,
  messageNode: MessageNode,
  consumerNode: ConsumerNode,
  sagaNode: SagaNode,
  activityNode: ActivityNode,
  routingSlipNode: RoutingSlipNode,
  groupNode: GroupNode
};

const getLayoutedElements = (data, filters, searchQuery, activeProjects, groupingEnabled, showMediatR) => {
  const messages = data.messages || [];
  const producers = data.producers || [];
  const consumers = data.consumers || [];
  const sagas = data.sagas || [];
  const activities = data.activities || [];
  const routingSlips = data.routingSlips || [];

  const initialFilteredMessages = messages.filter(msg => {
    const category = (msg.category || 'Event').toLowerCase();
    if (category === 'command' && !filters.command) return false;
    if (category === 'event' && !filters.event) return false;
    if (category === 'request' && !filters.request) return false;

    if (searchQuery) {
      return msg.type.toLowerCase().includes(searchQuery.toLowerCase());
    }
    return true;
  });

  const activeMessageTypesByFilters = new Set(initialFilteredMessages.map(m => m.type));

  const filteredProducers = producers.filter(p => {
    if (!showMediatR && p.provider === 'MediatR') return false;
    if (!activeMessageTypesByFilters.has(p.messageType)) return false;
    if (activeProjects && !activeProjects.has(p.project || 'Unknown')) return false;
    if (searchQuery) {
      const q = searchQuery.toLowerCase();
      return p.location.toLowerCase().includes(q) || p.messageType.toLowerCase().includes(q);
    }
    return true;
  });

  const filteredConsumers = consumers.filter(c => {
    if (!showMediatR && c.provider === 'MediatR') return false;
    if (!activeMessageTypesByFilters.has(c.messageType)) return false;
    if (activeProjects && !activeProjects.has(c.project || 'Unknown')) return false;
    if (searchQuery) {
      const q = searchQuery.toLowerCase();
      return c.consumerType.toLowerCase().includes(q) || c.messageType.toLowerCase().includes(q) || c.location.toLowerCase().includes(q);
    }
    return true;
  });

  const filteredSagas = sagas.filter(s => {
    if (activeProjects && !activeProjects.has(s.project || 'Unknown')) return false;
    const hasActiveEvent = s.consumedEvents.some(ev => activeMessageTypesByFilters.has(ev)) ||
                           s.publishedMessages.some(pub => activeMessageTypesByFilters.has(pub));
    if (!hasActiveEvent) return false;
    if (searchQuery) {
      const q = searchQuery.toLowerCase();
      return s.sagaType.toLowerCase().includes(q) || s.location.toLowerCase().includes(q);
    }
    return true;
  });

  const filteredActivities = activities.filter(a => {
    if (!activeMessageTypesByFilters.has(a.argumentsType)) return false;
    if (activeProjects && !activeProjects.has(a.project || 'Unknown')) return false;
    if (searchQuery) {
      const q = searchQuery.toLowerCase();
      return a.activityType.toLowerCase().includes(q) || a.argumentsType.toLowerCase().includes(q) || a.location.toLowerCase().includes(q);
    }
    return true;
  });

  const filteredRoutingSlips = routingSlips.filter(rs => {
    if (activeProjects && !activeProjects.has(rs.project || 'Unknown')) return false;
    if (searchQuery) {
      const q = searchQuery.toLowerCase();
      const inSteps = (rs.itinerary || []).some(s =>
        s.name.toLowerCase().includes(q) || (s.argumentsType || '').toLowerCase().includes(q));
      return inSteps || (rs.location || '').toLowerCase().includes(q);
    }
    return true;
  });

  // Mapa tipo-de-argumentos -> actividad, para enlazar cada paso de la slip con su IActivity.
  const actByArgs = {};
  filteredActivities.forEach(a => { actByArgs[a.argumentsType] = a.activityType; });

  const referencedMessages = new Set();
  filteredProducers.forEach(p => referencedMessages.add(p.messageType));
  filteredConsumers.forEach(c => referencedMessages.add(c.messageType));
  filteredSagas.forEach(s => {
    s.consumedEvents.forEach(ev => referencedMessages.add(ev));
    s.publishedMessages.forEach(pub => referencedMessages.add(pub));
  });
  filteredActivities.forEach(a => {
    referencedMessages.add(a.argumentsType);
    if (a.compensateLogType) referencedMessages.add(a.compensateLogType);
  });

  const finalMessages = initialFilteredMessages.filter(msg => referencedMessages.has(msg.type));
  const activeMessageTypes = new Set(finalMessages.map(m => m.type));

  const warnings = [];
  const messageWarningStates = {};
  finalMessages.forEach(msg => {
    const hasProducer = filteredProducers.some(p => p.messageType === msg.type) ||
                        filteredSagas.some(s => s.publishedMessages.includes(msg.type));
    const hasConsumer = filteredConsumers.some(c => c.messageType === msg.type) ||
                        filteredSagas.some(s => s.consumedEvents.includes(msg.type)) ||
                        filteredActivities.some(a => a.argumentsType === msg.type);
    
    messageWarningStates[msg.type] = { hasProducer, hasConsumer };
    
    if (!hasProducer || !hasConsumer) {
      warnings.push({
        type: msg.type,
        category: msg.category,
        hasProducer,
        hasConsumer
      });
    }
  });

  const nodes = [];
  const edges = [];

  // Routing slips: 5ª columna a la derecha. Cada tarjeta muestra su itinerario ordenado
  // internamente; además se enlaza cada paso con su nodo de actividad correspondiente.
  const rsX = 1620;
  const rsBaseHeight = 160;
  const rsStepHeight = 24;
  const rsGap = 50;
  const rsHeights = filteredRoutingSlips.map(rs => rsBaseHeight + (rs.itinerary?.length || 0) * rsStepHeight);
  const rsTotalHeight = rsHeights.reduce((a, b) => a + b, 0) + Math.max(0, filteredRoutingSlips.length - 1) * rsGap;

  const placeRoutingSlips = (maxHeight) => {
    let y = (maxHeight - rsTotalHeight) / 2 + 20;
    filteredRoutingSlips.forEach((rs, i) => {
      const rsId = `rs-${rs.location}`;
      nodes.push({
        id: rsId,
        type: 'routingSlipNode',
        position: { x: rsX, y },
        data: {
          location: rs.location,
          project: rs.project,
          itinerary: rs.itinerary || [],
          subscribedEvents: rs.subscribedEvents || [],
          isExecuted: rs.isExecuted,
          codeSnippet: rs.codeSnippet
        }
      });

      (rs.itinerary || []).forEach((step, sIdx) => {
        const actType = step.argumentsType && actByArgs[step.argumentsType];
        if (actType) {
          edges.push({
            id: `edge-act-${actType}-to-rs-${rs.location}-${sIdx}`,
            source: `act-${actType}`,
            sourceHandle: 'output',
            target: rsId,
            targetHandle: 'input',
            animated: true,
            style: { stroke: 'hsl(var(--routingslip-base))', strokeDasharray: '4 3' },
            markerEnd: { type: MarkerType.ArrowClosed, color: 'hsl(var(--routingslip-base))' }
          });
        }
      });
      y += rsHeights[i] + rsGap;
    });
  };

  const producersByLocation = {};
  filteredProducers.forEach(p => {
    if (!producersByLocation[p.location]) {
      producersByLocation[p.location] = {
        location: p.location,
        className: p.location.split(' (')[1]?.replace(')', '') || p.location.split(':')[0],
        methodName: p.location.match(/\.([A-Za-z0-9_]+)\)/)?.[1] || '',
        messages: [],
        project: p.project || 'Unknown',
        codeSnippet: p.codeSnippet,
        provider: p.provider || 'MassTransit'
      };
    }
    producersByLocation[p.location].messages.push(p);
  });
  const producerList = Object.values(producersByLocation);

  const consumersByType = {};
  filteredConsumers.forEach(c => {
    if (!consumersByType[c.consumerType]) {
      consumersByType[c.consumerType] = {
        consumerType: c.consumerType,
        location: c.location,
        messages: [],
        project: c.project || 'Unknown',
        codeSnippet: c.codeSnippet,
        provider: c.provider || 'MassTransit'
      };
    }
    consumersByType[c.consumerType].messages.push(c);
  });
  const consumerList = Object.values(consumersByType);

  const col4List = [
    ...consumerList.map(c => ({ ...c, isConsumer: true })),
    ...filteredActivities.map(a => ({ ...a, isActivity: true }))
  ];

  const nodeHeight = 110;
  const verticalGap = 50;
  const rowHeight = nodeHeight + verticalGap;

  if (groupingEnabled) {
    const containerPadding = 20;
    const innerGap = 20;
    const groupGap = 50;

    const prodsByProj = {};
    producerList.forEach(prod => {
      const proj = prod.project;
      if (!prodsByProj[proj]) prodsByProj[proj] = [];
      prodsByProj[proj].push(prod);
    });

    const sagasByProj = {};
    filteredSagas.forEach(saga => {
      const proj = saga.project || 'Unknown';
      if (!sagasByProj[proj]) sagasByProj[proj] = [];
      sagasByProj[proj].push(saga);
    });

    const col4ByProj = {};
    col4List.forEach(item => {
      const proj = item.project || 'Unknown';
      if (!col4ByProj[proj]) col4ByProj[proj] = [];
      col4ByProj[proj].push(item);
    });

    const getGroupHeight = (numItems) => containerPadding * 2 + numItems * nodeHeight + (numItems - 1) * innerGap;

    let col1Height = 0;
    const col1Groups = Object.keys(prodsByProj).map(proj => {
      const h = getGroupHeight(prodsByProj[proj].length);
      col1Height += h;
      return { project: proj, height: h, items: prodsByProj[proj] };
    });
    col1Height += (col1Groups.length - 1) * groupGap;

    let col3Height = 0;
    const col3Groups = Object.keys(sagasByProj).map(proj => {
      const h = getGroupHeight(sagasByProj[proj].length);
      col3Height += h;
      return { project: proj, height: h, items: sagasByProj[proj] };
    });
    col3Height += (col3Groups.length - 1) * groupGap;

    let col4Height = 0;
    const col4Groups = Object.keys(col4ByProj).map(proj => {
      const h = getGroupHeight(col4ByProj[proj].length);
      col4Height += h;
      return { project: proj, height: h, items: col4ByProj[proj] };
    });
    col4Height += (col4Groups.length - 1) * groupGap;

    const totalMessageHeight = finalMessages.length * rowHeight;
    const maxHeight = Math.max(col1Height, totalMessageHeight, col3Height, col4Height, rsTotalHeight, rowHeight);

    let runningY1 = 0;
    const offset1 = (maxHeight - col1Height) / 2;
    col1Groups.forEach(group => {
      const groupY = runningY1 + offset1;
      const groupId = `group-prods-${group.project}`;
      
      nodes.push({
        id: groupId,
        type: 'groupNode',
        position: { x: 80, y: groupY },
        style: { width: 290, height: group.height },
        data: { label: `${group.project} (Publishers)` }
      });

      group.items.forEach((prod, index) => {
        const childY = containerPadding + index * (nodeHeight + innerGap);
        nodes.push({
          id: `prod-${prod.location}`,
          type: 'producerNode',
          parentId: groupId,
          extent: 'parent',
          position: { x: 20, y: childY },
          data: {
            location: prod.location,
            className: prod.className,
            methodName: prod.methodName,
            project: prod.project,
            codeSnippet: prod.codeSnippet,
            provider: prod.provider
          }
        });

        prod.messages.forEach(m => {
          const edgeColor = prod.provider === 'MediatR' ? 'hsl(var(--mediatr-base))' : 'hsl(var(--producer-base))';
          edges.push({
            id: `edge-prod-${prod.location}-to-${m.messageType}`,
            source: `prod-${prod.location}`,
            target: `msg-${m.messageType}`,
            animated: true,
            style: { stroke: edgeColor },
            markerEnd: {
              type: MarkerType.ArrowClosed,
              color: edgeColor
            }
          });
        });
      });
      runningY1 += group.height + groupGap;
    });

    finalMessages.forEach((msg, index) => {
      const offset = (maxHeight - (finalMessages.length * rowHeight)) / 2;
      const y = index * rowHeight + offset + 20;

      const warningState = messageWarningStates[msg.type] || { hasProducer: true, hasConsumer: true };

      nodes.push({
        id: `msg-${msg.type}`,
        type: 'messageNode',
        position: { x: 460, y },
        data: {
          type: msg.type,
          category: msg.category,
          hasProducer: warningState.hasProducer,
          hasConsumer: warningState.hasConsumer
        }
      });
    });

    let runningY3 = 0;
    const offset3 = (maxHeight - col3Height) / 2;
    col3Groups.forEach(group => {
      const groupY = runningY3 + offset3;
      const groupId = `group-sagas-${group.project}`;

      nodes.push({
        id: groupId,
        type: 'groupNode',
        position: { x: 840, y: groupY },
        style: { width: 290, height: group.height },
        data: { label: `${group.project} (Sagas)` }
      });

      group.items.forEach((saga, index) => {
        const childY = containerPadding + index * (nodeHeight + innerGap);
        nodes.push({
          id: `saga-${saga.sagaType}`,
          type: 'sagaNode',
          parentId: groupId,
          extent: 'parent',
          position: { x: 20, y: childY },
          data: {
            sagaType: saga.sagaType,
            location: saga.location,
            project: saga.project,
            codeSnippet: saga.codeSnippet
          }
        });

        saga.consumedEvents.forEach(ev => {
          if (activeMessageTypes.has(ev)) {
            edges.push({
              id: `edge-msg-${ev}-to-saga-${saga.sagaType}`,
              source: `msg-${ev}`,
              target: `saga-${saga.sagaType}`,
              animated: true,
              style: { stroke: 'hsl(var(--saga-base))' },
              markerEnd: {
                type: MarkerType.ArrowClosed,
                color: 'hsl(var(--saga-base))'
              }
            });
          }
        });

        saga.publishedMessages.forEach(pub => {
          if (activeMessageTypes.has(pub)) {
            edges.push({
              id: `edge-saga-${saga.sagaType}-to-msg-${pub}`,
              source: `saga-${saga.sagaType}`,
              target: `msg-${pub}`,
              animated: true,
              style: { stroke: 'hsl(var(--saga-base))' },
              markerEnd: {
                type: MarkerType.ArrowClosed,
                color: 'hsl(var(--saga-base))'
              }
            });
          }
        });
      });
      runningY3 += group.height + groupGap;
    });

    let runningY4 = 0;
    const offset4 = (maxHeight - col4Height) / 2;
    col4Groups.forEach(group => {
      const groupY = runningY4 + offset4;
      const groupId = `group-consumers-${group.project}`;

      nodes.push({
        id: groupId,
        type: 'groupNode',
        position: { x: 1220, y: groupY },
        style: { width: 290, height: group.height },
        data: { label: `${group.project} (Consumers)` }
      });

      group.items.forEach((item, index) => {
        const childY = containerPadding + index * (nodeHeight + innerGap);
        
        if (item.isConsumer) {
          nodes.push({
            id: `cons-${item.consumerType}`,
            type: 'consumerNode',
            parentId: groupId,
            extent: 'parent',
            position: { x: 20, y: childY },
            data: {
              consumerType: item.consumerType,
              location: item.location,
              project: item.project,
              codeSnippet: item.codeSnippet,
              provider: item.provider            }
          });

          item.messages.forEach(m => {
            edges.push({
              id: `edge-msg-${m.messageType}-to-cons-${item.consumerType}`,
              source: `msg-${m.messageType}`,
              target: `cons-${item.consumerType}`,
              animated: true,
              style: { stroke: 'hsl(var(--consumer-base))' },
              markerEnd: {
                type: MarkerType.ArrowClosed,
                color: 'hsl(var(--consumer-base))'
              }
            });
          });
        } else {
          nodes.push({
            id: `act-${item.activityType}`,
            type: 'activityNode',
            parentId: groupId,
            extent: 'parent',
            position: { x: 20, y: childY },
            data: {
              activityType: item.activityType,
              location: item.location,
              argumentsType: item.argumentsType,
              project: item.project,
              codeSnippet: item.codeSnippet,
              provider: item.provider            }
          });

          if (activeMessageTypes.has(item.argumentsType)) {
            edges.push({
              id: `edge-msg-${item.argumentsType}-to-act-${item.activityType}`,
              source: `msg-${item.argumentsType}`,
              target: `act-${item.activityType}`,
              animated: true,
              style: { stroke: 'hsl(var(--activity-base))' },
              markerEnd: {
                type: MarkerType.ArrowClosed,
                color: 'hsl(var(--activity-base))'
              }
            });
          }
        }
      });
      runningY4 += group.height + groupGap;
    });

    placeRoutingSlips(maxHeight);
  } else {
    const totalProducerHeight = producerList.length * rowHeight;
    const totalMessageHeight = finalMessages.length * rowHeight;
    const totalSagaHeight = filteredSagas.length * rowHeight;
    const totalConsumerActivityHeight = col4List.length * rowHeight;

    const maxHeight = Math.max(
      totalProducerHeight,
      totalMessageHeight,
      totalSagaHeight,
      totalConsumerActivityHeight,
      rsTotalHeight,
      rowHeight
    );

    producerList.forEach((prod, index) => {
      const offset = (maxHeight - (producerList.length * rowHeight)) / 2;
      const y = index * rowHeight + offset + 20;

      nodes.push({
        id: `prod-${prod.location}`,
        type: 'producerNode',
        position: { x: 80, y },
        data: {
          location: prod.location,
          className: prod.className,
          methodName: prod.methodName,
          project: prod.project,
          codeSnippet: prod.codeSnippet,
          provider: prod.provider
        }
      });

      prod.messages.forEach(m => {
        const edgeColor = prod.provider === 'MediatR' ? 'hsl(var(--mediatr-base))' : 'hsl(var(--producer-base))';
        edges.push({
          id: `edge-prod-${prod.location}-to-${m.messageType}`,
          source: `prod-${prod.location}`,
          target: `msg-${m.messageType}`,
          animated: true,
          style: { stroke: edgeColor },
          markerEnd: {
            type: MarkerType.ArrowClosed,
            color: edgeColor
          }
        });
      });
    });

    finalMessages.forEach((msg, index) => {
      const offset = (maxHeight - (finalMessages.length * rowHeight)) / 2;
      const y = index * rowHeight + offset + 20;

      const warningState = messageWarningStates[msg.type] || { hasProducer: true, hasConsumer: true };

      nodes.push({
        id: `msg-${msg.type}`,
        type: 'messageNode',
        position: { x: 460, y },
        data: {
          type: msg.type,
          category: msg.category,
          hasProducer: warningState.hasProducer,
          hasConsumer: warningState.hasConsumer
        }
      });
    });

    filteredSagas.forEach((saga, index) => {
      const offset = (maxHeight - (filteredSagas.length * rowHeight)) / 2;
      const y = index * rowHeight + offset + 20;

      nodes.push({
        id: `saga-${saga.sagaType}`,
        type: 'sagaNode',
        position: { x: 840, y },
        data: {
          sagaType: saga.sagaType,
          location: saga.location,
          project: saga.project,
          codeSnippet: saga.codeSnippet
        }
      });

      saga.consumedEvents.forEach(ev => {
        if (activeMessageTypes.has(ev)) {
          edges.push({
            id: `edge-msg-${ev}-to-saga-${saga.sagaType}`,
            source: `msg-${ev}`,
            target: `saga-${saga.sagaType}`,
            animated: true,
            style: { stroke: 'hsl(var(--saga-base))' },
            markerEnd: {
              type: MarkerType.ArrowClosed,
              color: 'hsl(var(--saga-base))'
            }
          });
        }
      });

      saga.publishedMessages.forEach(pub => {
        if (activeMessageTypes.has(pub)) {
          edges.push({
            id: `edge-saga-${saga.sagaType}-to-msg-${pub}`,
            source: `saga-${saga.sagaType}`,
            target: `msg-${pub}`,
            animated: true,
            style: { stroke: 'hsl(var(--saga-base))' },
            markerEnd: {
              type: MarkerType.ArrowClosed,
              color: 'hsl(var(--saga-base))'
            }
          });
        }
      });
    });

    col4List.forEach((item, index) => {
      const offset = (maxHeight - (col4List.length * rowHeight)) / 2;
      const y = index * rowHeight + offset + 20;

      if (item.isConsumer) {
        nodes.push({
          id: `cons-${item.consumerType}`,
          type: 'consumerNode',
          position: { x: 1220, y },
          data: {
            consumerType: item.consumerType,
            location: item.location,
            project: item.project,
            codeSnippet: item.codeSnippet,
            provider: item.provider
          }
        });

        item.messages.forEach(m => {
          edges.push({
            id: `edge-msg-${m.messageType}-to-cons-${item.consumerType}`,
            source: `msg-${m.messageType}`,
            target: `cons-${item.consumerType}`,
            animated: true,
            style: { stroke: 'hsl(var(--consumer-base))' },
            markerEnd: {
              type: MarkerType.ArrowClosed,
              color: 'hsl(var(--consumer-base))'
            }
          });
        });
      } else {
        nodes.push({
          id: `act-${item.activityType}`,
          type: 'activityNode',
          position: { x: 1220, y },
          data: {
            activityType: item.activityType,
            location: item.location,
            argumentsType: item.argumentsType,
            project: item.project,
            codeSnippet: item.codeSnippet,
            provider: item.provider
          }
        });

        if (activeMessageTypes.has(item.argumentsType)) {
          edges.push({
            id: `edge-msg-${item.argumentsType}-to-act-${item.activityType}`,
            source: `msg-${item.argumentsType}`,
            target: `act-${item.activityType}`,
            animated: true,
            style: { stroke: 'hsl(var(--activity-base))' },
            markerEnd: {
              type: MarkerType.ArrowClosed,
              color: 'hsl(var(--activity-base))'
            }
          });
        }
      }
    });

    placeRoutingSlips(maxHeight);
  }

  return { nodes, edges, warnings };
};

export default function App() {
  const [data, setData] = useState({ messages: [], producers: [], consumers: [], sagas: [], activities: [], routingSlips: [] });
  const [loading, setLoading] = useState(true);
  const [searchQuery, setSearchQuery] = useState('');
  const [filters, setFilters] = useState({
    command: true,
    event: true,
    request: true
  });
  const [selectedNode, setSelectedNode] = useState(null);
  const [selectedProjects, setSelectedProjects] = useState(null);
  const [groupingEnabled, setGroupingEnabled] = useState(true);
  const [showMediatR, setShowMediatR] = useState(true);

  React.useEffect(() => {
    fetch('/api/topology')
      .then(res => {
        if (!res.ok) throw new Error('API server not available');
        return res.json();
      })
      .then(jsonData => {
        setData(jsonData);
        setLoading(false);
      })
      .catch(err => {
        console.warn("No se pudo conectar a la API del CLI, usando datos estáticos de muestra:", err);
        setData(initialData);
        setLoading(false);
      });
  }, []);

  // Calcular la lista de todos los proyectos únicos en los datos
  const allProjects = useMemo(() => {
    const projects = new Set();
    (data.producers || []).forEach(p => { if (p.project) projects.add(p.project); });
    (data.consumers || []).forEach(c => { if (c.project) projects.add(c.project); });
    (data.sagas || []).forEach(s => { if (s.project) projects.add(s.project); });
    (data.activities || []).forEach(a => { if (a.project) projects.add(a.project); });
    (data.routingSlips || []).forEach(rs => { if (rs.project) projects.add(rs.project); });
    return Array.from(projects).sort();
  }, [data]);

  // Si selectedProjects es null, tratamos todos los proyectos como seleccionados
  const activeProjects = useMemo(() => {
    if (selectedProjects === null) {
      return new Set(allProjects);
    }
    return selectedProjects;
  }, [selectedProjects, allProjects]);

  // Generar nodos y aristas base
  const { nodes: rawNodes, edges: rawEdges, warnings } = useMemo(() => {
    return getLayoutedElements(data, filters, searchQuery, activeProjects, groupingEnabled, showMediatR);
  }, [data, filters, searchQuery, activeProjects, groupingEnabled, showMediatR]);

  // Algoritmo de Path Analysis para resaltar flujos (upstream/downstream)
  const highlightedElements = useMemo(() => {
    if (!selectedNode) return null;

    const highlightedNodes = new Set([selectedNode.id]);
    const highlightedEdges = new Set();

    // Traversal hacia adelante (downstream)
    const traverseForward = (currentId) => {
      rawEdges.forEach(edge => {
        if (edge.source === currentId && !highlightedEdges.has(edge.id)) {
          highlightedEdges.add(edge.id);
          highlightedNodes.add(edge.target);
          traverseForward(edge.target);
        }
      });
    };

    // Traversal hacia atrás (upstream)
    const traverseBackward = (currentId) => {
      rawEdges.forEach(edge => {
        if (edge.target === currentId && !highlightedEdges.has(edge.id)) {
          highlightedEdges.add(edge.id);
          highlightedNodes.add(edge.source);
          traverseBackward(edge.source);
        }
      });
    };

    traverseForward(selectedNode.id);
    traverseBackward(selectedNode.id);

    return { nodes: highlightedNodes, edges: highlightedEdges };
  }, [selectedNode, rawEdges]);

  // Aplicar atenuación / resaltado dinámico de nodos
  const nodes = useMemo(() => {
    if (!highlightedElements) return rawNodes;
    return rawNodes.map(node => {
      const isHighlighted = highlightedElements.nodes.has(node.id);
      return {
        ...node,
        style: {
          ...node.style,
          opacity: isHighlighted ? 1 : 0.15,
          transition: 'opacity 0.3s ease, border-color 0.3s ease, box-shadow 0.3s ease'
        }
      };
    });
  }, [rawNodes, highlightedElements]);

  // Aplicar atenuación / resaltado dinámico de aristas
  const edges = useMemo(() => {
    if (!highlightedElements) return rawEdges;
    return rawEdges.map(edge => {
      const isHighlighted = highlightedElements.edges.has(edge.id);
      return {
        ...edge,
        animated: isHighlighted,
        style: {
          ...edge.style,
          opacity: isHighlighted ? 1 : 0.08,
          strokeWidth: isHighlighted ? 3 : 1.5,
          transition: 'opacity 0.3s ease, stroke-width 0.3s ease'
        }
      };
    });
  }, [rawEdges, highlightedElements]);

  // Manejar la carga de un nuevo JSON
  const handleDataLoaded = (newData) => {
    setData(newData);
    setSelectedNode(null);
    setSelectedProjects(null);
  };

  // Conteo por filtro (sobre el total de datos) para dar feedback de la distribución:
  // así se ve por qué al desmarcar Commands o MediatR puede vaciarse el grafo.
  const filterCounts = useMemo(() => {
    const msgs = data.messages || [];
    const byCat = { command: 0, event: 0, request: 0 };
    msgs.forEach(m => {
      const c = (m.category || 'Event').toLowerCase();
      if (c in byCat) byCat[c]++;
    });
    const mediatrMsgs = new Set();
    (data.producers || []).forEach(p => { if (p.provider === 'MediatR') mediatrMsgs.add(p.messageType); });
    (data.consumers || []).forEach(c => { if (c.provider === 'MediatR') mediatrMsgs.add(c.messageType); });
    return {
      command: byCat.command,
      event: byCat.event,
      request: byCat.request,
      mediatr: mediatrMsgs.size,
      total: msgs.length
    };
  }, [data]);

  // Calcular estadísticas para el Sidebar
  const stats = useMemo(() => {
    const messages = data.messages || [];
    const producers = data.producers || [];
    const consumers = data.consumers || [];

    return {
      messages: messages.length,
      producers: producers.length + (data.sagas || []).length, // Sumamos sagas que actúan de publicadoras
      consumers: consumers.length + (data.activities || []).length, // Sumamos actividades
      orphaned: warnings ? warnings.length : 0,
      warnings: warnings || []
    };
  }, [data, warnings]);

  // Selección de nodos
  const onNodeClick = (event, node) => {
    setSelectedNode(node);
  };

  const onPaneClick = () => {
    setSelectedNode(null);
  };

  const handleSelectNodeById = (nodeId) => {
    const node = rawNodes.find(n => n.id === nodeId);
    if (node) {
      setSelectedNode(node);
    }
  };

  return (
    <div className="app-container">
      {loading && (
        <div style={{
          position: 'absolute',
          inset: 0,
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          backgroundColor: 'hsl(var(--bg-primary))',
          color: 'hsl(var(--text-primary))',
          zIndex: 1000,
          fontFamily: 'sans-serif'
        }}>
          <div style={{ fontSize: '20px', fontWeight: '600', letterSpacing: '0.05em', color: 'hsl(var(--producer-base))', marginBottom: '12px' }}>
            Message Flow Explorer
          </div>
          <div style={{ fontSize: '13px', opacity: 0.6 }}>Cargando topología de mensajería...</div>
        </div>
      )}
      {/* Sidebar */}
      <Sidebar
        stats={stats}
        filterCounts={filterCounts}
        searchQuery={searchQuery}
        setSearchQuery={setSearchQuery}
        filters={filters}
        setFilters={setFilters}
        onDataLoaded={handleDataLoaded}
        allProjects={allProjects}
        selectedProjects={activeProjects}
        onSelectedProjectsChange={setSelectedProjects}
        groupingEnabled={groupingEnabled}
        onGroupingEnabledChange={setGroupingEnabled}
        showMediatR={showMediatR}
        onShowMediatRChange={setShowMediatR}
        onSelectNodeById={handleSelectNodeById}
      />

      {/* React Flow Canvas */}
      <div className="canvas-container">
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodeTypes={nodeTypes}
          onNodeClick={onNodeClick}
          onPaneClick={onPaneClick}
          fitView
          attributionPosition="bottom-right"
        >
          <Background color="hsla(var(--border) / 0.3)" gap={16} size={1} />
          <Controls />
          <MiniMap 
            nodeColor={(node) => {
              if (node.type === 'producerNode') return 'hsl(var(--producer-base))';
              if (node.type === 'consumerNode') return 'hsl(var(--consumer-base))';
              if (node.type === 'sagaNode') return 'hsl(var(--saga-base))';
              if (node.type === 'activityNode') return 'hsl(var(--activity-base))';
              if (node.type === 'routingSlipNode') return 'hsl(var(--routingslip-base))';
              if (node.type === 'messageNode') {
                if (node.data.category === 'Command') return 'hsl(var(--command-base))';
                if (node.data.category === 'Request') return 'hsl(var(--request-base))';
                return 'hsl(var(--event-base))';
              }
              return 'hsl(220 13% 75%)';
            }}
            maskColor="hsla(var(--bg-primary) / 0.7)"
            className="border border-white/[0.05] rounded-lg overflow-hidden !bg-black/30 backdrop-blur-md"
          />
        </ReactFlow>
      </div>

      {/* Panel de Detalles */}
      <DetailsPanel
        node={selectedNode}
        allData={data}
        onClose={() => setSelectedNode(null)}
      />
    </div>
  );
}
