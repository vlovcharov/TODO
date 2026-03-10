import { useState, useRef, useEffect, useCallback } from 'react';
import { X, GripVertical } from 'lucide-react';
import { scheduleApi } from '../api';
import { formatDateParam } from '../constants';

const START_HOUR = 6;
const END_HOUR   = 22;
const SLOT_MINS  = 30;
const TOTAL_MINS = (END_HOUR - START_HOUR) * 60;
const SLOT_HEIGHT = 36; // px per 30-min slot
const TOTAL_HEIGHT = (TOTAL_MINS / SLOT_MINS) * SLOT_HEIGHT;

function minutesToY(mins) {
  return ((mins - START_HOUR * 60) / SLOT_MINS) * SLOT_HEIGHT;
}

function yToMinutes(y) {
  const raw = Math.round((y / SLOT_HEIGHT) * SLOT_MINS) + START_HOUR * 60;
  return Math.max(START_HOUR * 60, Math.min(END_HOUR * 60, raw));
}

function formatTime(mins) {
  const h = Math.floor(mins / 60);
  const m = mins % 60;
  return `${h.toString().padStart(2, '0')}:${m.toString().padStart(2, '0')}`;
}

function ScheduleBlock({ block, onUpdate, onDelete }) {
  const top    = minutesToY(block.startMinutes);
  const height = Math.max(SLOT_HEIGHT, minutesToY(block.endMinutes) - top);
  const resizing = useRef(false);
  const startY   = useRef(0);
  const startEnd = useRef(0);

  const handleResizeStart = (e) => {
    e.stopPropagation();
    resizing.current  = true;
    startY.current    = e.clientY;
    startEnd.current  = block.endMinutes;

    const onMove = (ev) => {
      if (!resizing.current) return;
      const dy   = ev.clientY - startY.current;
      const dm   = Math.round((dy / SLOT_HEIGHT) * SLOT_MINS);
      const newEnd = Math.max(block.startMinutes + SLOT_MINS,
                              Math.min(END_HOUR * 60, startEnd.current + dm));
      // Snap to 30-min slots
      const snapped = Math.round(newEnd / SLOT_MINS) * SLOT_MINS;
      onUpdate({ ...block, endMinutes: snapped });
    };

    const onUp = async () => {
      resizing.current = false;
      document.removeEventListener('mousemove', onMove);
      document.removeEventListener('mouseup', onUp);
      // Persist
      await scheduleApi.update(block.id, {
        date:         block.date,
        taskId:       block.taskId ?? null,
        label:        block.label,
        startMinutes: block.startMinutes,
        endMinutes:   block.endMinutes,
      });
    };

    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup', onUp);
  };

  return (
    <div
      className="schedule-block"
      style={{ top, height, '--block-color': block.color ?? '#6366f1' }}
    >
      <div className="schedule-block-label">
        <span>{block.label}</span>
        <span className="schedule-block-time">
          {formatTime(block.startMinutes)}–{formatTime(block.endMinutes)}
        </span>
      </div>
      <button
        className="schedule-block-delete"
        onClick={() => onDelete(block.id)}
        title="Remove"
      >
        <X size={11} />
      </button>
      <div className="schedule-block-resize" onMouseDown={handleResizeStart} />
    </div>
  );
}

export default function DayPlanner({ date, tasks, onBlocksChange }) {
  const [blocks, setBlocks]     = useState([]);
  const [draggingTask, setDragging] = useState(null);
  const [ghostY, setGhostY]     = useState(null);
  const [freeformLabel, setFreeformLabel] = useState('');
  const [freeformSlot, setFreeformSlot]   = useState(null);
  const gridRef = useRef(null);
  const dateStr = formatDateParam(date);

  const load = useCallback(async () => {
    const data = await scheduleApi.getForDate(dateStr);
    setBlocks(data);
  }, [dateStr]);

  useEffect(() => { load(); }, [load]);

  const getYInGrid = (clientY) => {
    const rect = gridRef.current?.getBoundingClientRect();
    if (!rect) return 0;
    return Math.max(0, Math.min(TOTAL_HEIGHT, clientY - rect.top));
  };

  // ── Task pill drag from task list ─────────────────────────────────────────
  const handleGridDragOver = (e) => {
    e.preventDefault();
    setGhostY(getYInGrid(e.clientY));
  };

  const handleGridDrop = async (e) => {
    e.preventDefault();
    const taskId = e.dataTransfer.getData('text/task-id');
    const label  = e.dataTransfer.getData('text/task-title');
    if (!taskId && !label) return;

    const y = getYInGrid(e.clientY);
    const startMinutes = Math.round(yToMinutes(y) / SLOT_MINS) * SLOT_MINS;
    const endMinutes   = startMinutes + 60;

    const block = await scheduleApi.create({
      date:         dateStr,
      taskId:       taskId || null,
      label:        label,
      startMinutes,
      endMinutes,
    });
    setBlocks(prev => [...prev, block]);
    setGhostY(null);
    onBlocksChange?.();
  };

  // ── Click on empty slot = freeform ────────────────────────────────────────
  const handleGridClick = (e) => {
    if (e.target !== gridRef.current && !e.target.classList.contains('planner-slot-line')) return;
    const y = getYInGrid(e.clientY);
    const startMinutes = Math.round(yToMinutes(y) / SLOT_MINS) * SLOT_MINS;
    setFreeformSlot(startMinutes);
    setFreeformLabel('');
  };

  const handleFreeformSubmit = async () => {
    if (!freeformLabel.trim() || freeformSlot === null) return;
    const block = await scheduleApi.create({
      date:         dateStr,
      taskId:       null,
      label:        freeformLabel.trim(),
      startMinutes: freeformSlot,
      endMinutes:   freeformSlot + 60,
    });
    setBlocks(prev => [...prev, block]);
    setFreeformSlot(null);
    onBlocksChange?.();
  };

  const handleBlockUpdate = (updated) => {
    setBlocks(prev => prev.map(b => b.id === updated.id ? updated : b));
  };

  const handleBlockDelete = async (id) => {
    await scheduleApi.delete(id);
    setBlocks(prev => prev.filter(b => b.id !== id));
  };

  // Time labels on the left
  const timeLabels = [];
  for (let h = START_HOUR; h <= END_HOUR; h++) {
    timeLabels.push(h);
  }

  return (
    <div className="day-planner">
      <div className="epics-panel-header">
        <span className="epics-panel-title">Day Planner</span>
        <span className="planner-hint">Drag tasks or click to add</span>
      </div>

      <div className="planner-scroll">
        <div className="planner-inner">
          {/* Time labels */}
          <div className="planner-times">
            {timeLabels.map(h => (
              <div key={h} className="planner-time-label" style={{ top: minutesToY(h * 60) }}>
                {h.toString().padStart(2, '0')}:00
              </div>
            ))}
          </div>

          {/* Grid */}
          <div
            ref={gridRef}
            className="planner-grid"
            style={{ height: TOTAL_HEIGHT }}
            onDragOver={handleGridDragOver}
            onDragLeave={() => setGhostY(null)}
            onDrop={handleGridDrop}
            onClick={handleGridClick}
          >
            {/* Hour lines */}
            {timeLabels.map(h => (
              <div
                key={h}
                className="planner-slot-line"
                style={{ top: minutesToY(h * 60) }}
              />
            ))}
            {/* Half-hour lines */}
            {timeLabels.slice(0, -1).map(h => (
              <div
                key={`${h}h`}
                className="planner-slot-line half"
                style={{ top: minutesToY(h * 60 + 30) }}
              />
            ))}

            {/* Drop ghost */}
            {ghostY !== null && (
              <div className="planner-drop-ghost" style={{ top: Math.round(ghostY / SLOT_HEIGHT) * SLOT_HEIGHT }} />
            )}

            {/* Freeform input */}
            {freeformSlot !== null && (
              <div className="planner-freeform-input" style={{ top: minutesToY(freeformSlot) }}>
                <input
                  autoFocus
                  placeholder="Label..."
                  value={freeformLabel}
                  onChange={e => setFreeformLabel(e.target.value)}
                  onKeyDown={e => {
                    if (e.key === 'Enter') handleFreeformSubmit();
                    if (e.key === 'Escape') setFreeformSlot(null);
                  }}
                  onBlur={() => freeformLabel.trim() ? handleFreeformSubmit() : setFreeformSlot(null)}
                />
              </div>
            )}

            {/* Blocks */}
            {blocks.map(block => (
              <ScheduleBlock
                key={block.id}
                block={block}
                onUpdate={handleBlockUpdate}
                onDelete={handleBlockDelete}
              />
            ))}
          </div>
        </div>
      </div>

      {/* Draggable task list */}
      <div className="planner-task-source">
        <div className="planner-task-source-title">Tasks — drag to schedule</div>
        {tasks.filter(t => !t.parentId && !t.isMissed && !t.isCompleted).map(t => (
          <div
            key={t.id}
            className="planner-task-pill"
            draggable
            onDragStart={e => {
              e.dataTransfer.setData('text/task-id', t.id);
              e.dataTransfer.setData('text/task-title', t.title);
            }}
          >
            <GripVertical size={12} className="planner-pill-grip" />
            <span>{t.title}</span>
          </div>
        ))}
      </div>
    </div>
  );
}
