import { useState } from 'react';
import { Plus, Pencil, Trash2, X, Check, ChevronDown, ChevronUp } from 'lucide-react';
import { epicsApi } from '../api';
import { isCompletedOnDate, formatDateParam } from '../constants';

function ColorPicker({ value, onChange }) {
  const PALETTE = [
    '#ef4444','#f97316','#f59e0b','#84cc16',
    '#10b981','#06b6d4','#6366f1','#8b5cf6',
    '#ec4899','#64748b','#0ea5e9','#14b8a6',
  ];
  return (
    <div className="color-picker">
      {PALETTE.map(c => (
        <button
          key={c}
          type="button"
          className={`color-swatch ${value === c ? 'selected' : ''}`}
          style={{ background: c }}
          onClick={() => onChange(c)}
        />
      ))}
    </div>
  );
}

function EpicForm({ epic, onSave, onCancel }) {
  const [title, setTitle] = useState(epic?.title ?? '');
  const [color, setColor] = useState(epic?.color ?? '#6366f1');

  const handleSave = async () => {
    if (!title.trim()) return;
    await onSave({ title: title.trim(), color });
  };

  return (
    <div className="epic-form">
      <input
        autoFocus
        className="epic-form-input"
        placeholder="Epic title..."
        value={title}
        onChange={e => setTitle(e.target.value)}
        onKeyDown={e => e.key === 'Enter' && handleSave()}
      />
      <ColorPicker value={color} onChange={setColor} />
      <div className="epic-form-actions">
        <button className="btn btn-ghost btn-xs" onClick={onCancel}>Cancel</button>
        <button className="btn btn-primary btn-xs" onClick={handleSave} disabled={!title.trim()}>
          {epic ? 'Save' : 'Create'}
        </button>
      </div>
    </div>
  );
}

export default function EpicsPanel({ epics, tasks, currentDate, onEpicsChange, onTasksChange }) {
  const [creating, setCreating] = useState(false);
  const [editingId, setEditingId] = useState(null);
  const [collapsed, setCollapsed] = useState(false);

  const dateStr = formatDateParam(currentDate);

  const handleCreate = async (data) => {
    await epicsApi.create(data);
    onEpicsChange();
    setCreating(false);
  };

  const handleUpdate = async (id, data) => {
    await epicsApi.update(id, data);
    onEpicsChange();
    setEditingId(null);
  };

  const handleDelete = async (id) => {
    if (!confirm('Delete this epic? Tasks linked to it will be unlinked.')) return;
    await epicsApi.delete(id);
    onEpicsChange();
    onTasksChange();
  };

  return (
    <div className="epics-panel">
      <div className="panel-header" onClick={() => setCollapsed(c => !c)} style={{ cursor: 'pointer' }}>
        <span className="epics-panel-title">Epics</span>
        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
          {!collapsed && (
            <button
              className="add-task-btn"
              onClick={e => { e.stopPropagation(); setCreating(true); setCollapsed(false); }}
              title="New epic"
            >
              <Plus size={14} />
            </button>
          )}
          {collapsed ? <ChevronDown size={14} /> : <ChevronUp size={14} />}
        </div>
      </div>

      {!collapsed && (
        <>
          {creating && (
            <EpicForm
              onSave={handleCreate}
              onCancel={() => setCreating(false)}
            />
          )}

          {epics.length === 0 && !creating && (
            <div className="epics-empty">No epics yet</div>
          )}

          {epics.map(epic => {
            // All tasks in this epic (top-level only)
            const allEpicTasks = tasks.filter(t => t.epicId === epic.id && !t.parentId);

            // Deduplicate rollover chains: for tasks sharing the same originalScheduledDate
            // (or their own id if never rolled over), only keep the active (non-missed) copy.
            // If the whole chain is missed (e.g. all in the past), keep the latest copy.
            const chainMap = new Map();
            for (const t of allEpicTasks) {
              const chainKey = t.originalScheduledDate ?? t.id;
              const existing = chainMap.get(chainKey);
              if (!existing) {
                chainMap.set(chainKey, t);
              } else {
                // Prefer non-missed; among same status prefer higher rolloverCount
                const preferNew = (!t.isMissed && existing.isMissed) ||
                  (t.isMissed === existing.isMissed && (t.rolloverCount ?? 0) > (existing.rolloverCount ?? 0));
                if (preferNew) chainMap.set(chainKey, t);
              }
            }
            const epicTasks = [...chainMap.values()];
            const doneTasks = epicTasks.filter(t => isCompletedOnDate(t, currentDate));

            return (
              <div key={epic.id} className="epic-card" style={{ '--epic-color': epic.color }}>
                {editingId === epic.id ? (
                  <EpicForm
                    epic={epic}
                    onSave={data => handleUpdate(epic.id, data)}
                    onCancel={() => setEditingId(null)}
                  />
                ) : (
                  <>
                    <div className="epic-card-header">
                      <div className="epic-color-dot" style={{ background: epic.color }} />
                      <span className="epic-card-title">{epic.title}</span>
                      <span className="epic-card-count">
                        {doneTasks.length}/{epicTasks.length}
                      </span>
                      <div className="epic-card-actions">
                        <button className="action-btn" onClick={() => setEditingId(epic.id)} title="Edit">
                          <Pencil size={12} />
                        </button>
                        <button className="action-btn danger" onClick={() => handleDelete(epic.id)} title="Delete">
                          <Trash2 size={12} />
                        </button>
                      </div>
                    </div>

                    {epicTasks.length > 0 && (
                      <div className="epic-task-list">
                        {epicTasks.map(t => {
                          const done = isCompletedOnDate(t, currentDate);
                          return (
                            <div
                              key={t.id}
                              className={`epic-task-pill ${done ? 'done' : ''}`}
                              style={{ borderLeftColor: epic.color }}
                            >
                              <span className={done ? 'completed' : ''}>{t.title}</span>
                              {done && <Check size={11} style={{ color: epic.color, flexShrink: 0 }} />}
                            </div>
                          );
                        })}
                      </div>
                    )}

                    {epicTasks.length > 0 && (
                      <div className="epic-progress-bar">
                        <div
                          className="epic-progress-fill"
                          style={{
                            width: `${(doneTasks.length / epicTasks.length) * 100}%`,
                            background: epic.color,
                          }}
                        />
                      </div>
                    )}
                  </>
                )}
              </div>
            );
          })}
        </>
      )}
    </div>
  );
}
