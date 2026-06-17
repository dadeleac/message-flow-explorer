import React, { useRef } from 'react';
import { Upload, Search, BarChart3, Filter, ShieldAlert } from 'lucide-react';

export default function Sidebar({
  stats,
  searchQuery,
  setSearchQuery,
  filters,
  setFilters,
  onDataLoaded,
  allProjects = [],
  selectedProjects = new Set(),
  onSelectedProjectsChange,
  groupingEnabled = true,
  onGroupingEnabledChange,
  showMediatR = true,
  onShowMediatRChange,
  onSelectNodeById
}) {
  const fileInputRef = useRef(null);

  const handleFileChange = (e) => {
    const file = e.target.files[0];
    if (!file) return;

    const reader = new FileReader();
    reader.onload = (event) => {
      try {
        const parsed = JSON.parse(event.target.result);
        onDataLoaded(parsed);
      } catch (err) {
        alert("Error al parsear el archivo JSON: " + err.message);
      }
    };
    reader.readAsText(file);
  };

  const toggleFilter = (key) => {
    setFilters(prev => ({
      ...prev,
      [key]: !prev[key]
    }));
  };

  const toggleProject = (project) => {
    if (!onSelectedProjectsChange) return;
    const next = new Set(selectedProjects);
    if (next.has(project)) {
      next.delete(project);
    } else {
      next.add(project);
    }
    onSelectedProjectsChange(next);
  };

  const selectAllProjects = () => {
    if (!onSelectedProjectsChange) return;
    onSelectedProjectsChange(new Set(allProjects));
  };

  const selectNoProjects = () => {
    if (!onSelectedProjectsChange) return;
    onSelectedProjectsChange(new Set());
  };

  return (
    <div className="sidebar glass-panel-heavy">
      {/* Header */}
      <div className="sidebar-header">
        <h1 className="sidebar-title">Message Flow Explorer</h1>
        <p className="sidebar-subtitle">Visualiza el flujo de mensajes en .NET</p>
      </div>

      {/* Cargar Archivo */}
      <div className="file-uploader" onClick={() => fileInputRef.current.click()}>
        <Upload size={20} className="file-uploader-icon" />
        <span className="file-uploader-text">Cargar message-flow.json</span>
        <input 
          type="file" 
          ref={fileInputRef} 
          onChange={handleFileChange} 
          accept=".json" 
          className="hidden-input" 
        />
      </div>

      {/* Estadísticas */}
      <div className="sidebar-section">
        <div className="sidebar-section-title">
          <BarChart3 size={14} />
          <span>Estadísticas</span>
        </div>
        <div className="stats-grid">
          <div className="stat-card">
            <div className="stat-value">{stats.messages}</div>
            <div className="stat-label">Mensajes</div>
          </div>
          <div className="stat-card">
            <div className="stat-value">{stats.producers}</div>
            <div className="stat-label">Publicadores</div>
          </div>
          <div className="stat-card">
            <div className="stat-value">{stats.consumers}</div>
            <div className="stat-label">Consumidores</div>
          </div>
        </div>
      </div>

      {/* Filtros */}
      <div className="sidebar-section">
        <div className="sidebar-section-title">
          <Filter size={14} />
          <span>Filtros de Mensaje</span>
        </div>
        <div className="filter-list">
          <label className="filter-item border-b border-white/[0.05] pb-2 mb-2">
            <div className="filter-info">
              <span className="filter-text font-bold text-primary">Agrupar por Proyecto</span>
            </div>
            <input 
              type="checkbox" 
              checked={groupingEnabled} 
              onChange={(e) => onGroupingEnabledChange && onGroupingEnabledChange(e.target.checked)}
              className="filter-checkbox"
            />
          </label>
          <label className="filter-item">
            <div className="filter-info">
              <span className="filter-circle bg-command"></span>
              <span className="filter-text">Commands</span>
            </div>
            <input 
              type="checkbox" 
              checked={filters.command} 
              onChange={() => toggleFilter('command')}
              className="filter-checkbox"
            />
          </label>
          <label className="filter-item">
            <div className="filter-info">
              <span className="filter-circle bg-event"></span>
              <span className="filter-text">Events</span>
            </div>
            <input 
              type="checkbox" 
              checked={filters.event} 
              onChange={() => toggleFilter('event')}
              className="filter-checkbox"
            />
          </label>
          <label className="filter-item">
            <div className="filter-info">
              <span className="filter-circle bg-request"></span>
              <span className="filter-text">Requests / Responses</span>
            </div>
            <input 
              type="checkbox" 
              checked={filters.request} 
              onChange={() => toggleFilter('request')}
              className="filter-checkbox"
            />
          </label>
          <label className="filter-item border-t border-white/[0.05] pt-2 mt-2">
            <div className="filter-info">
              <span className="filter-circle" style={{ backgroundColor: 'hsl(263 70% 58%)' }}></span>
              <span className="filter-text">MediatR (Flujos Locales)</span>
            </div>
            <input 
              type="checkbox" 
              checked={showMediatR} 
              onChange={(e) => onShowMediatRChange && onShowMediatRChange(e.target.checked)}
              className="filter-checkbox"
            />
          </label>
        </div>
      </div>

      {/* Filtros por Proyecto / Microservicio */}
      {allProjects.length > 0 && (
        <div className="sidebar-section">
          <div className="sidebar-section-title">
            <Filter size={14} />
            <span>Proyectos / Microservicios</span>
          </div>
          <div className="filter-actions-row">
            <button onClick={selectAllProjects} className="btn-small">Todos</button>
            <button onClick={selectNoProjects} className="btn-small">Ninguno</button>
          </div>
          <div className="filter-list select-project-list">
            {allProjects.map(project => (
              <label key={project} className="filter-item">
                <div className="filter-info">
                  <span className="filter-text font-semibold">{project}</span>
                </div>
                <input 
                  type="checkbox" 
                  checked={selectedProjects.has(project)} 
                  onChange={() => toggleProject(project)}
                  className="filter-checkbox"
                />
              </label>
            ))}
          </div>
        </div>
      )}

      {/* Buscador */}
      <div className="sidebar-section">
        <div className="sidebar-section-title">
          <Search size={14} />
          <span>Búsqueda</span>
        </div>
        <div className="search-container">
          <input 
            type="text" 
            placeholder="Buscar mensaje, actor, etc..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="search-input"
          />
          <Search size={14} className="search-icon" />
        </div>
      </div>

      {/* Advertencia / Insights */}
      {stats.orphaned > 0 && (
        <div className="warnings-section">
          <div className="sidebar-section-title">
            <ShieldAlert size={14} />
            <span>Alertas de Integración ({stats.orphaned})</span>
          </div>
          <div className="warnings-list">
            {stats.warnings.map(warn => (
              <div 
                key={warn.type}
                className="warning-item"
                onClick={() => onSelectNodeById && onSelectNodeById('msg-' + warn.type)}
              >
                <div className="warning-header">
                  <span className="warning-message-type" title={warn.type}>
                    {warn.type.split('.').pop() || warn.type}
                  </span>
                  <span className={`category-badge-mini ${warn.category.toLowerCase()}`}>
                    {warn.category}
                  </span>
                </div>
                <div className="warning-details">
                  {!warn.hasProducer && <span className="warning-badge">Sin Publicador</span>}
                  {!warn.hasConsumer && <span className="warning-badge">Sin Consumidor</span>}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
