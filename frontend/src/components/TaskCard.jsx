import { useState } from 'react';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { ChevronDown, ChevronRight, Plus, Trash2, GripVertical, ArrowRight, Pencil, CalendarClock } from 'lucide-react';
import { LEVELS, PRIORITIES, isCompletedOnDate, formatDateParam, getNextDay, getNextPeriod, recurrenceLabel } from '../constants';
import { tasksApi } from '../api';
import CreateTaskModal from './CreateTaskModal';

// Colour ramp: green (1 rollover) → yellow → orange → red (many rollovers)
function dotColor(index, total) {
  if (total <= 1) return '#22c55e';
  const pct = index / (total - 1);
  if (pct < 0.33) return '#22c55e';
  if (pct < 0.55) return '#84cc16';
  if (pct < 0.70) return '#eab308';
  if (pct < 0.85) return '#f97316';
  return '#ef4444';
}

const MAX_DOTS = 20;

function RolloverTrail({ count, originalDate }) {
  if (!count || count === 0) return null;

  const shown    = Math.min(count, MAX_DOTS);
  const overflow = count - shown;

  const originalStr = originalDate
    ? new Date(originalDate + 'T00:00:00').toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })
    : null;

  const tooltip = originalStr
    ? `Rolled over ${count}x — originally due ${originalStr}`
    : `Rolled over ${count}x`;

  return (
    <div className="rollover-trail" title={tooltip}>
      {Array.from({ length: shown }, (_, i) => (
        <div
          key={i}
          className="rollover-dot"
          style={{ background: dotColor(i, count) }}
        />
      ))}
      {overflow > 0 && (
        <span className="rollover-trail-overflow">+{overflow}</span>
      )}
    </div>
  );
}

export default function TaskCard({ task, allTasks, currentDate, epics = [], onUpdate, onDelete, onCreate, depth = 0 }) {
  const [expanded, setExpanded] = useState(false);
  const [showActions, setShowActions] = useState(false);
  const [editing, setEditing] = useState(false);

  const { attributes, listeners, setNodeRef, transform, transition, isDragging } =
    useSortable({ id: task.id });

  const style = { transform: CSS.Transform.toString(transform), transition, opacity: isDragging ? 0.4 : 1 };

  const subtasks    = allTasks.filter(t => t.parentId === task.id);
  const isCompleted = isCompletedOnDate(task, currentDate);
  const level       = LEVELS[task.level]        || LEVELS.Daily;
  const priority    = PRIORITIES[task.priority] || PRIORITIES.Average;
  const isRecurring = (task.recurrenceMask ?? 0) !== 0;
  const recurLabel  = recurrenceLabel(task.recurrenceMask ?? 0);

  // Epic color overrides the left border when depth === 0
  const epic = depth === 0 && task.epicId ? epics.find(e => e.id === task.epicId) : null;
  const borderColor = epic ? epic.color : level.color;

  const handleToggle = async () => {
    const dateParam = isRecurring ? formatDateParam(currentDate) : undefined;
    const all = await tasksApi.toggle(task.id, dateParam);
    onUpdate(null, all);
  };

  const handleDelete = async () => {
    await tasksApi.delete(task.id);
    onDelete(task.id);
  };

  // Move to tomorrow (always safe, no confirmation needed)
  const handleMoveTomorrow = async () => {
    const tomorrow = getNextDay(new Date(task.scheduledDate + 'T00:00:00'));
    const updated  = await tasksApi.move(task.id, formatDateParam(tomorrow));
    onUpdate(updated);
  };

  // Move to next natural period (week/month/year) — requires confirmation for non-daily
  const handleMoveNextPeriod = async () => {
    const taskDate = new Date(task.scheduledDate + 'T00:00:00');
    const nextDate = getNextPeriod(task.level, taskDate);
    const daysDiff = Math.round((nextDate - taskDate) / (1000 * 60 * 60 * 24));
    if (daysDiff > 1) {
      const levelLabel = LEVELS[task.level]?.label ?? task.level;
      if (!confirm(`Move "${task.title}" to next ${levelLabel.toLowerCase()} period?\nThis will push it ${daysDiff} days forward.`)) return;
    }
    const updated = await tasksApi.move(task.id, formatDateParam(nextDate));
    onUpdate(updated);
  };

  return (
    <>
      <div
        ref={setNodeRef}
        style={{ ...style, '--task-border-color': borderColor }}
        className={`task-card ${isCompleted ? 'task-done' : ''} ${isDragging ? 'dragging' : ''} depth-${Math.min(depth, 3)} ${epic ? 'has-epic' : ''}`}
        onMouseEnter={() => setShowActions(true)}
        onMouseLeave={() => setShowActions(false)}
      >
        <div className="task-row"
          draggable
          onDragStart={e => {
            e.dataTransfer.setData('text/task-id', task.id);
            e.dataTransfer.setData('text/task-title', task.title);
            e.stopPropagation();
          }}
        >
          <div className="drag-handle" {...attributes} {...listeners}>
            <GripVertical size={14} />
          </div>

          {subtasks.length > 0 ? (
            <button className="expand-btn" onClick={() => setExpanded(e => !e)}>
              {expanded ? <ChevronDown size={13} /> : <ChevronRight size={13} />}
            </button>
          ) : <div style={{ width: 18 }} />}

          <button
            className={`checkbox ${isCompleted ? 'checked' : ''}`}
            onClick={handleToggle}
            style={{ borderColor: borderColor }}
          >
            {isCompleted && (
              <svg width="10" height="8" viewBox="0 0 10 8">
                <path d="M1 4l3 3 5-6" stroke="white" strokeWidth="1.8" fill="none" strokeLinecap="round" strokeLinejoin="round"/>
              </svg>
            )}
          </button>

          <span className="level-badge" style={{ background: level.bg, color: level.color }}>
            {level.short}
          </span>

          <span className={`task-title ${isCompleted ? 'completed' : ''}`}>
            {task.title}
            {isRecurring && (
              <span className="recurring-dot" title={recurLabel}>↻ {recurLabel}</span>
            )}
            {subtasks.length > 0 && (() => {
              const doneSubs = subtasks.filter(s => isCompletedOnDate(s, currentDate)).length;
              return (
                <span className={`subtask-progress ${doneSubs === subtasks.length ? 'all-done' : ''}`}>
                  {doneSubs}/{subtasks.length}
                </span>
              );
            })()}
          </span>

          <span className="priority-icon" style={{ color: priority.color }} title={priority.label}>
            {priority.icon}
          </span>

          <div className={`task-actions ${showActions ? 'visible' : ''}`}>
            <button className="action-btn" onClick={() => setEditing(true)} title="Edit">
              <Pencil size={13} />
            </button>
            {!isRecurring && (
              <>
                <button className="action-btn" onClick={handleMoveTomorrow} title="Move to tomorrow">
                  <ArrowRight size={13} />
                </button>
                {task.level !== 'Daily' && (
                  <button className="action-btn" onClick={handleMoveNextPeriod} title={`Move to next ${LEVELS[task.level]?.label ?? ''} period`}>
                    <CalendarClock size={13} />
                  </button>
                )}
              </>
            )}
            <button
              className="action-btn"
              onClick={() => onCreate({ parentId: task.id, level: task.level, scheduledDate: task.scheduledDate })}
              title="Add subtask"
            >
              <Plus size={13} />
            </button>
            <button className="action-btn danger" onClick={handleDelete} title="Delete">
              <Trash2 size={13} />
            </button>
          </div>
        </div>

        {task.description && !isCompleted && (
          <div className="task-desc">{task.description}</div>
        )}

        <RolloverTrail count={task.rolloverCount} originalDate={task.originalScheduledDate} />

        {expanded && subtasks.length > 0 && (
          <div className="subtasks">
            {subtasks.map(sub => (
              <TaskCard
                key={sub.id}
                task={sub}
                allTasks={allTasks}
                currentDate={currentDate}
                epics={epics}
                onUpdate={onUpdate}
                onDelete={onDelete}
                onCreate={onCreate}
                depth={depth + 1}
              />
            ))}
          </div>
        )}
      </div>

      {editing && (
        <CreateTaskModal
          task={task}
          epics={epics}
          onUpdated={updated => onUpdate(updated)}
          onClose={() => setEditing(false)}
        />
      )}
    </>
  );
}
