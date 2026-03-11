import { useState, useRef, useEffect, useCallback } from 'react';
import { X, ChevronDown, ChevronUp } from 'lucide-react';
import { scheduleApi } from '../api';
import { formatDateParam } from '../constants';

const START_HOUR  = 6;
const END_HOUR    = 24;
const SLOT_MINS   = 30;
const TOTAL_MINS  = (END_HOUR - START_HOUR) * 60;
const SLOT_HEIGHT = 36; // px per 30-min slot
const TOP_PADDING = 10; // px so 06:00 label isn't clipped
const TOTAL_HEIGHT = (TOTAL_MINS / SLOT_MINS) * SLOT_HEIGHT + TOP_PADDING;

function minutesToY(mins) {
  return ((mins - START_HOUR * 60) / SLOT_MINS) * SLOT_HEIGHT + TOP_PADDING;
}

function yToMinutes(y) {
  const raw = Math.round(((y - TOP_PADDING) / SLOT_HEIGHT) * SLOT_MINS) + START_HOUR * 60;
  return Math.max(START_HOUR * 60, Math.min(END_HOUR * 60, raw));
}

function snapToSlot(mins) {
  return Math.round(mins / SLOT_MINS) * SLOT_MINS;
}

function formatTime(mins) {
  const h = Math.floor(mins / 60) % 24;
  const m = mins % 60;
  return `${h.toString().padStart(2, '0')}:${m.toString().padStart(2, '0')}`;
}

function ScheduleBlock({ block, onUpdate, onDelete, onMoveCommit }) {
  const top    = minutesToY(block.startMinutes);
  const height = Math.max(SLOT_HEIGHT, minutesToY(block.endMinutes) - minutesToY(block.startMinutes));
  const duration = block.endMinutes - block.startMinutes;
  const resizing  = useRef(false);
  const dragging  = useRef(false);
  const startY    = useRef(0);
  const startEnd  = useRef(0);
  const startStart = useRef(0);

  // ── Resize from bottom edge ────────────────────────────────────────────
  const handleResizeStart = (e) => {
    e.stopPropagation();
    resizing.current = true;
    startY.current   = e.clientY;
    startEnd.current = block.endMinutes;

    const onMove = (ev) => {
      if (!resizing.current) return;
      const dy  = ev.clientY - startY.current;
      const dm  = Math.round((dy / SLOT_HEIGHT) * SLOT_MINS);
      const snapped = snapToSlot(Math.max(block.startMinutes + SLOT_MINS,
                                          Math.min(END_HOUR * 60, startEnd.current + dm)));
      onUpdate({ ...block, endMinutes: snapped });
    };

    const onUp = async () => {
      resizing.current = false;
      document.removeEventListener('mousemove', onMove);
      document.removeEventListener('mouseup', onUp);
      await onMoveCommit(block);
    };

    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup', onUp);
  };

  // ── Drag block to new time ─────────────────────────────────────────────
  const handleDragStart = (e) => {
    if (resizing.current) return;
    dragging.current  = true;
    startY.current    = e.clientY;
    startStart.current = block.startMinutes;

    const onMove = (ev) => {
      if (!dragging.current) return;
      const dy  = ev.clientY - startY.current;
      const dm  = Math.round((dy / SLOT_HEIGHT) * SLOT_MINS);
      const newStart = snapToSlot(Math.max(START_HOUR * 60,
                                           Math.min(END_HOUR * 60 - duration, startStart.current + dm)));
      onUpdate({ ...block, startMinutes: newStart, endMinutes: newStart + duration });
    };

    const onUp = async () => {
      dragging.current = false;
      document.removeEventListener('mousemove', onMove);
      document.removeEventListener('mouseup', onUp);
      await onMoveCommit(block);
    };

    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup', onUp);
  };

  return (
    <div
      className="schedule-block"
      style={{ top, height, '--block-color': block.color ?? '#6366f1', cursor: 'grab' }}
      onMouseDown={handleDragStart}
    >
      <div className="schedule-block-label">
        <span>{block.label}</span>
        <span className="schedule-block-time">
          {formatTime(block.startMinutes)}–{formatTime(block.endMinutes)}
        </span>
      </div>
      <button
        className="schedule-block-delete"
        onMouseDown={e => e.stopPropagation()}
        onClick={() => onDelete(block.id)}
        title="Remove"
      >
        <X size={11} />
      </button>
      <div className="schedule-block-resize" onMouseDown={handleResizeStart} />
    </div>
  );
}

export default function DayPlanner({ date, tasks }) {
  const [blocks, setBlocks]         = useState([]);
  const [collapsed, setCollapsed]   = useState(false);
  const [ghostY, setGhostY]         = useState(null);
  const [freeformLabel, setFreeform] = useState('');
  const [freeformSlot, setSlot]     = useState(null);
  const gridRef = useRef(null);
  const dateStr = formatDateParam(date);

  const load = useCallback(async () => {
    const data = await scheduleApi.getForDate(dateStr);
    setBlocks(data);
  }, [dateStr]);

  useEffect(() => { load(); }, [load]);

  const getYInGrid = (clientY) => {
    const rect = gridRef.current?.getBoundingClientRect();
    if (!rect) return TOP_PADDING;
    return Math.max(TOP_PADDING, Math.min(TOTAL_HEIGHT, clientY - rect.top));
  };

  const handleGridDragOver = (e) => {
    e.preventDefault();
    setGhostY(getYInGrid(e.clientY));
  };

  const handleGridDrop = async (e) => {
    e.preventDefault();
    const taskId = e.dataTransfer.getData('text/task-id');
    const label  = e.dataTransfer.getData('text/task-title');
    if (!taskId && !label) return;

    const y            = getYInGrid(e.clientY);
    const startMinutes = snapToSlot(yToMinutes(y));
    const endMinutes   = Math.min(END_HOUR * 60, startMinutes + 30);

    const block = await scheduleApi.create({
      date: dateStr, taskId: taskId || null, label,
      startMinutes, endMinutes,
    });
    setBlocks(prev => [...prev, block]);
    setGhostY(null);
  };

  const handleGridClick = (e) => {
    if (e.target !== gridRef.current && !e.target.classList.contains('planner-slot-line')) return;
    const y = getYInGrid(e.clientY);
    setSlot(snapToSlot(yToMinutes(y)));
    setFreeform('');
  };

  const handleFreeformSubmit = async () => {
    if (!freeformLabel.trim() || freeformSlot === null) return;
    const block = await scheduleApi.create({
      date: dateStr, taskId: null, label: freeformLabel.trim(),
      startMinutes: freeformSlot,
      endMinutes: Math.min(END_HOUR * 60, freeformSlot + 30),
    });
    setBlocks(prev => [...prev, block]);
    setSlot(null);
  };

  const handleBlockUpdate = (updated) => {
    setBlocks(prev => prev.map(b => b.id === updated.id ? updated : b));
  };

  const handleMoveCommit = async (block) => {
    await scheduleApi.update(block.id, {
      date: block.date, taskId: block.taskId ?? null,
      label: block.label, startMinutes: block.startMinutes, endMinutes: block.endMinutes,
    });
  };

  const handleBlockDelete = async (id) => {
    await scheduleApi.delete(id);
    setBlocks(prev => prev.filter(b => b.id !== id));
  };

  const timeLabels = [];
  for (let h = START_HOUR; h <= END_HOUR; h++) timeLabels.push(h);

  return (
    <div className="day-planner">
      <div className="panel-header" onClick={() => setCollapsed(c => !c)} style={{ cursor: 'pointer' }}>
        <span className="epics-panel-title">Day Planner</span>
        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
          {!collapsed && <span className="planner-hint">Drag tasks · click to add</span>}
          {collapsed ? <ChevronDown size={14} /> : <ChevronUp size={14} />}
        </div>
      </div>

      {!collapsed && (
        <div className="planner-scroll">
          <div className="planner-inner">
            <div className="planner-times">
              {timeLabels.map(h => (
                <div key={h} className="planner-time-label" style={{ top: minutesToY(h * 60) }}>
                  {h === 24 ? '00:00' : `${h.toString().padStart(2, '0')}:00`}
                </div>
              ))}
            </div>

            <div
              ref={gridRef}
              className="planner-grid"
              style={{ height: TOTAL_HEIGHT }}
              onDragOver={handleGridDragOver}
              onDragLeave={() => setGhostY(null)}
              onDrop={handleGridDrop}
              onClick={handleGridClick}
            >
              {timeLabels.map(h => (
                <div key={h} className="planner-slot-line" style={{ top: minutesToY(h * 60) }} />
              ))}
              {timeLabels.slice(0, -1).map(h => (
                <div key={`${h}h`} className="planner-slot-line half" style={{ top: minutesToY(h * 60 + 30) }} />
              ))}

              {ghostY !== null && (
                <div className="planner-drop-ghost" style={{ top: Math.round((ghostY - TOP_PADDING) / SLOT_HEIGHT) * SLOT_HEIGHT + TOP_PADDING }} />
              )}

              {freeformSlot !== null && (
                <div className="planner-freeform-input" style={{ top: minutesToY(freeformSlot) }}>
                  <input
                    autoFocus
                    placeholder="Label..."
                    value={freeformLabel}
                    onChange={e => setFreeform(e.target.value)}
                    onKeyDown={e => {
                      if (e.key === 'Enter') handleFreeformSubmit();
                      if (e.key === 'Escape') setSlot(null);
                    }}
                    onBlur={() => freeformLabel.trim() ? handleFreeformSubmit() : setSlot(null)}
                  />
                </div>
              )}

              {blocks.map(block => (
                <ScheduleBlock
                  key={block.id}
                  block={block}
                  onUpdate={handleBlockUpdate}
                  onDelete={handleBlockDelete}
                  onMoveCommit={handleMoveCommit}
                />
              ))}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
