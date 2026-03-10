import { useState, useRef } from 'react';
import { X } from 'lucide-react';
import { LEVELS, PRIORITIES, formatDateParam, RECURRENCE_PRESETS, DAY_BITS, DAY_LABELS } from '../constants';
import { tasksApi } from '../api';

function RecurrencePicker({ mask, onChange }) {
  const isCustom = mask !== 0 && !RECURRENCE_PRESETS.find(p => p.mask === mask && p.mask !== -1);
  const selectedPreset = isCustom ? -1 : mask;

  const handlePreset = (val) => {
    if (val === -1) {
      // Switch to custom — start with whatever days are currently set, or Mon
      onChange(mask > 0 ? mask : DAY_BITS.Mon);
    } else {
      onChange(val);
    }
  };

  const toggleDay = (bit) => {
    const next = mask ^ bit;
    onChange(next < 0 ? 0 : next);
  };

  return (
    <div className="recurrence-picker">
      <select
        className="form-input"
        value={selectedPreset}
        onChange={e => handlePreset(Number(e.target.value))}
      >
        {RECURRENCE_PRESETS.map(p => (
          <option key={p.mask} value={p.mask}>{p.label}</option>
        ))}
        {isCustom && <option value={-1}>Custom…</option>}
      </select>

      {(isCustom || selectedPreset === -1) && (
        <div className="day-toggle-row">
          {DAY_LABELS.map((label, i) => {
            const bit = 1 << i;
            const active = (mask & bit) !== 0;
            return (
              <button
                key={label}
                type="button"
                className={`day-toggle-btn ${active ? 'active' : ''}`}
                onClick={() => toggleDay(bit)}
              >
                {label}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}

export default function CreateTaskModal({
  defaultDate, defaultLevel, parentId,
  task,
  onCreated, onUpdated, onClose,
  initialCreateAnother = false,
}) {
  const isEdit = !!task;
  const titleRef = useRef(null);

  const blankForm = (overrides = {}) => ({
    title:         '',
    description:   '',
    level:         defaultLevel || 'Daily',
    priority:      'Average',
    recurrenceMask: 0,
    scheduledDate: defaultDate ? formatDateParam(defaultDate) : formatDateParam(new Date()),
    ...overrides,
  });

  const [form, setForm] = useState(
    isEdit ? {
      title:         task.title,
      description:   task.description ?? '',
      level:         task.level,
      priority:      task.priority,
      recurrenceMask: task.recurrenceMask ?? 0,
      scheduledDate: task.scheduledDate,
    } : blankForm()
  );
  const [loading, setLoading] = useState(false);
  const [createAnother, setCreateAnother] = useState(initialCreateAnother);

  const set = (k, v) => setForm(f => ({ ...f, [k]: v }));

  const handleSubmit = async () => {
    if (!form.title.trim()) return;
    setLoading(true);
    try {
      const payload = {
        title:         form.title.trim(),
        description:   form.description.trim() || null,
        level:         form.level,
        priority:      form.priority,
        recurrenceMask: form.recurrenceMask,
        scheduledDate: form.recurrenceMask !== 0 ? null : form.scheduledDate,
      };

      if (isEdit) {
        const updated = await tasksApi.update(task.id, { ...payload, description: form.description.trim() });
        onUpdated?.(updated);
        onClose();
      } else {
        const created = await tasksApi.create({ ...payload, parentId: parentId || null });
        onCreated?.(created);
        if (createAnother) {
          setForm(f => blankForm({ level: f.level, priority: f.priority, recurrenceMask: f.recurrenceMask, scheduledDate: f.scheduledDate }));
          setTimeout(() => titleRef.current?.focus(), 0);
        } else {
          onClose();
        }
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal animate-in" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <h2>{isEdit ? 'Edit Task' : parentId ? 'Add Subtask' : 'New Task'}</h2>
          <button className="modal-close" onClick={onClose}><X size={18} /></button>
        </div>

        <div className="modal-body">
          <div className="form-group">
            <label>Title *</label>
            <input
              ref={titleRef}
              autoFocus
              className="form-input"
              placeholder="What needs to be done?"
              value={form.title}
              onChange={e => set('title', e.target.value)}
              onKeyDown={e => e.key === 'Enter' && handleSubmit()}
            />
          </div>

          <div className="form-group">
            <label>Description</label>
            <textarea
              className="form-input"
              rows={2}
              placeholder="Optional details..."
              value={form.description}
              onChange={e => set('description', e.target.value)}
            />
          </div>

          <div className="form-row">
            <div className="form-group">
              <label>Level</label>
              <select className="form-input" value={form.level} onChange={e => set('level', e.target.value)}>
                {Object.entries(LEVELS).map(([k, v]) => (
                  <option key={k} value={k}>{v.label}</option>
                ))}
              </select>
            </div>
            <div className="form-group">
              <label>Priority</label>
              <select className="form-input" value={form.priority} onChange={e => set('priority', e.target.value)}>
                {Object.entries(PRIORITIES).map(([k, v]) => (
                  <option key={k} value={k}>{v.label}</option>
                ))}
              </select>
            </div>
          </div>

          <div className="form-group">
            <label>Recurrence</label>
            <RecurrencePicker
              mask={form.recurrenceMask}
              onChange={v => set('recurrenceMask', v)}
            />
          </div>

          {form.recurrenceMask === 0 && (
            <div className="form-group">
              <label>Date</label>
              <input
                type="date"
                className="form-input"
                value={form.scheduledDate}
                onChange={e => set('scheduledDate', e.target.value)}
              />
            </div>
          )}
        </div>

        <div className="modal-footer">
          {!isEdit && (
            <label className="create-another-label">
              <input
                type="checkbox"
                checked={createAnother}
                onChange={e => setCreateAnother(e.target.checked)}
              />
              Create another
            </label>
          )}
          <button className="btn btn-ghost" onClick={onClose}>Cancel</button>
          <button
            className="btn btn-primary"
            onClick={handleSubmit}
            disabled={loading || !form.title.trim()}
          >
            {loading ? 'Saving...' : isEdit ? 'Save Changes' : 'Create Task'}
          </button>
        </div>
      </div>
    </div>
  );
}
