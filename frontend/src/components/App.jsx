import { useState, useEffect, useCallback } from 'react';
import { addDays, addWeeks, addMonths, addYears, subDays, subWeeks, subMonths, subYears, format } from 'date-fns';
import { ChevronLeft, ChevronRight, Plus, Calendar, Download, Upload } from 'lucide-react';
import { VIEWS, getViewRange, formatDateParam } from '../constants';
import { tasksApi, configApi, backupApi, epicsApi } from '../api';
import { DayView, WeekView, MonthView, YearView } from '../components/CalendarViews';
import CreateTaskModal from '../components/CreateTaskModal';
import StatsPanel from '../components/StatsPanel';
import './App.css';

export default function App() {
  const [view, setView] = useState('Day');
  const [anchor, setAnchor] = useState(new Date());
  const [tasks, setTasks] = useState([]);
  const [epics, setEpics] = useState([]);
  const [loading, setLoading] = useState(true);
  const [modal, setModal] = useState(null);

  const loadTasks = useCallback(async () => {
    try {
      const all = await tasksApi.getAll();
      setTasks(all);
    } finally {
      setLoading(false);
    }
  }, []);

  const loadEpics = useCallback(async () => {
    const all = await epicsApi.getAll();
    setEpics(all);
  }, []);

  useEffect(() => { loadTasks(); loadEpics(); }, [loadTasks, loadEpics]);

  const handleUpdate = useCallback((updated, allTasks) => {
    if (allTasks) { setTasks(allTasks); return; }
    if (!updated) { loadTasks(); return; }
    setTasks(prev => {
      const idx = prev.findIndex(t => t.id === updated.id);
      if (idx >= 0) { const n = [...prev]; n[idx] = updated; return n; }
      return [...prev, updated];
    });
  }, [loadTasks]);

  const handleDelete = useCallback((id) => {
    setTasks(prev => prev.filter(t => t.id !== id && !t.subtaskIds?.includes(id)));
  }, []);

  const handleCreate = useCallback((opts) => { setModal(opts); }, []);
  const handleCreated = useCallback(() => { loadTasks(); }, [loadTasks]);

  const handleExportJson = async () => {
    const res = await backupApi.exportJson();
    const url = URL.createObjectURL(res.data);
    const a = document.createElement('a'); a.href = url;
    a.download = `taskflow-export-${new Date().toISOString().slice(0,10)}.json`;
    a.click(); URL.revokeObjectURL(url);
  };

  const handleExportCsv = async () => {
    const res = await backupApi.exportCsv();
    const url = URL.createObjectURL(res.data);
    const a = document.createElement('a'); a.href = url;
    a.download = `taskflow-export-${new Date().toISOString().slice(0,10)}.csv`;
    a.click(); URL.revokeObjectURL(url);
  };

  const handleImport = (e) => {
    const file = e.target.files?.[0];
    if (!file) return;
    backupApi.import(file).then(result => {
      alert(`Import complete: ${result.imported} imported, ${result.skipped} skipped.`);
      loadTasks();
    }).catch(err => alert(`Import failed: ${err.response?.data || err.message}`));
    e.target.value = '';
  };

  const navigate = (dir) => {
    const d = dir === 1;
    setAnchor(prev => {
      switch (view) {
        case 'Day':   return d ? addDays(prev, 1)   : subDays(prev, 1);
        case 'Week':  return d ? addWeeks(prev, 1)  : subWeeks(prev, 1);
        case 'Month': return d ? addMonths(prev, 1) : subMonths(prev, 1);
        case 'Year':  return d ? addYears(prev, 1)  : subYears(prev, 1);
        default: return prev;
      }
    });
  };

  const getTitle = () => {
    switch (view) {
      case 'Day':   return format(anchor, 'EEEE, MMMM d, yyyy');
      case 'Week': {
        const { from, to } = getViewRange('Week', anchor);
        return `${format(from, 'MMM d')} – ${format(to, 'MMM d, yyyy')}`;
      }
      case 'Month': return format(anchor, 'MMMM yyyy');
      case 'Year':  return format(anchor, 'yyyy');
    }
  };

  const viewProps = { anchor, tasks, epics, onUpdate: handleUpdate, onDelete: handleDelete, onCreate: handleCreate, onEpicsChange: loadEpics };

  return (
    <div className="app">
      <aside className="sidebar">
        <div className="sidebar-logo">
          <Calendar size={20} />
          <span>TaskFlow</span>
        </div>

        <nav className="sidebar-views">
          {VIEWS.map(v => (
            <button key={v} className={`sidebar-view-btn ${view === v ? 'active' : ''}`} onClick={() => setView(v)}>
              {v} View
            </button>
          ))}
        </nav>

        <button className="sidebar-new-btn" onClick={() => setModal({ scheduledDate: anchor })}>
          <Plus size={16} />
          New Task
        </button>

        <div className="sidebar-legend">
          <div className="legend-title">Levels</div>
          {Object.entries({ Daily: '#6366f1', Weekly: '#0ea5e9', Monthly: '#10b981', Yearly: '#f59e0b' }).map(([k, c]) => (
            <div key={k} className="legend-item">
              <div className="legend-dot" style={{ background: c }} />
              <span>{k}</span>
            </div>
          ))}
        </div>

        <StatsPanel tasks={tasks} anchor={anchor} />
      </aside>

      <main className="main">
        <header className="topbar">
          <div className="topbar-nav">
            <button className="nav-btn" onClick={() => navigate(-1)}><ChevronLeft size={18} /></button>
            <button className="today-btn" onClick={() => { setAnchor(new Date()); setView('Day'); }}>Today</button>
            <button className="nav-btn" onClick={() => navigate(1)}><ChevronRight size={18} /></button>
            <h1 className="topbar-title">{getTitle()}</h1>
          </div>
          <div className="topbar-right">
            <div className="topbar-actions">
              <div className="export-group">
                <button className="btn-icon" title="Export JSON" onClick={handleExportJson}><Download size={15} /><span>JSON</span></button>
                <button className="btn-icon" title="Export CSV" onClick={handleExportCsv}><Download size={15} /><span>CSV</span></button>
                <label className="btn-icon" title="Import">
                  <Upload size={15} /><span>Import</span>
                  <input type="file" accept=".json,.csv" onChange={handleImport} style={{ display: 'none' }} />
                </label>
              </div>
            </div>
            <div className="view-tabs">
              {VIEWS.map(v => (
                <button key={v} className={`view-tab ${view === v ? 'active' : ''}`} onClick={() => setView(v)}>{v}</button>
              ))}
            </div>
          </div>
        </header>

        <div className="calendar-area">
          {loading ? (
            <div className="loading">Loading tasks...</div>
          ) : (
            <>
              {view === 'Day'   && <DayView {...viewProps} />}
              {view === 'Week'  && <WeekView {...viewProps} onNavigate={(v, d) => { setView(v); setAnchor(d); }} />}
              {view === 'Month' && <MonthView {...viewProps} onNavigate={(v, d) => { setView(v); setAnchor(d); }} />}
              {view === 'Year'  && <YearView {...viewProps} onNavigate={(v, d) => { setView(v); setAnchor(d); }} />}
            </>
          )}
        </div>
      </main>

      {modal && (
        <CreateTaskModal
          defaultDate={modal.scheduledDate instanceof Date ? modal.scheduledDate : new Date(modal.scheduledDate + 'T00:00:00')}
          defaultLevel={modal.level}
          parentId={modal.parentId}
          epics={epics}
          onCreated={handleCreated}
          onClose={() => setModal(null)}
        />
      )}
    </div>
  );
}
