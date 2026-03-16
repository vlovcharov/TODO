import { useState, useCallback } from 'react';
import {
  DndContext,
  DragOverlay,
  closestCenter,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
} from '@dnd-kit/core';
import {
  SortableContext,
  sortableKeyboardCoordinates,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable';
import { format, isToday, isBefore, startOfDay } from 'date-fns';
import { Plus } from 'lucide-react';
import TaskCard from './TaskCard';
import { tasksApi } from '../api';
import { isRecurringActiveOnDate, isCompletedOnDate, isTaskStickyOnDate, formatDateParam } from '../constants';
import './TaskCard.css';

export default function DayColumn({ date, allTasks, epics = [], onUpdate, onDelete, onCreate, onClickHeader, compact = false }) {
  const [activeId, setActiveId] = useState(null);
  const isCurrentDay = isToday(date);
  const isPast = isBefore(startOfDay(date), startOfDay(new Date()));

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates })
  );

  const dateStr = formatDateParam(date);

  // Tasks scheduled exactly on this date
  const ownTasks = allTasks.filter(t => {
    if (t.parentId) return false;
    if ((t.recurrenceMask ?? 0) !== 0) return isRecurringActiveOnDate(t, date);
    return t.scheduledDate === dateStr;
  });

  // Sticky tasks: weekly/monthly/yearly from the same period, shown every day until done
  const stickyTasks = allTasks
    .filter(t => isTaskStickyOnDate(t, date))
    .map(t => ({ ...t, _sticky: true }));

  // Merge — own tasks take priority (avoid duplicates)
  const ownIds = new Set(ownTasks.map(t => t.id));
  const dayTasks = [
    ...ownTasks,
    ...stickyTasks.filter(t => !ownIds.has(t.id)),
  ].sort((a, b) => a.sortOrder - b.sortOrder);

  const doneTasks   = dayTasks.filter(t => isCompletedOnDate(t, date));
  const missedTasks = dayTasks.filter(t => t.isMissed && !isCompletedOnDate(t, date));
  const activeTasks = dayTasks.filter(t => !isCompletedOnDate(t, date) && !t.isMissed);
  const activeTaskIds = activeTasks.map(t => t.id);

  const activeTask = allTasks.find(t => t.id === activeId);

  const handleDragStart = ({ active }) => setActiveId(active.id);

  const handleDragEnd = async ({ active, over }) => {
    setActiveId(null);
    if (!over || active.id === over.id) return;

    const oldIndex = activeTaskIds.indexOf(active.id);
    const newIndex = activeTaskIds.indexOf(over.id);
    if (oldIndex === -1 || newIndex === -1) return;

    const reordered = [...activeTaskIds];
    reordered.splice(oldIndex, 1);
    reordered.splice(newIndex, 0, active.id);

    await tasksApi.reorder(reordered);
    onUpdate(null);
  };

  const cardProps = (task) => ({
    task,
    allTasks,
    currentDate: date,
    epics,
    isPast,
    isSticky: !!task._sticky,
    onUpdate,
    onDelete,
    onCreate,
  });

  return (
    <div className={`day-column ${isCurrentDay ? 'today' : ''} ${compact ? 'compact' : ''}`}>
      <div className="day-header" onClick={onClickHeader} style={onClickHeader ? { cursor: 'pointer' } : undefined}>
        <div className="day-header-content">
          <span className="day-name">{format(date, compact ? 'EEE' : 'EEEE')}</span>
          <span className={`day-number ${isCurrentDay ? 'today-badge' : ''}`}>
            {format(date, 'd')}
          </span>
          {dayTasks.length > 0 && (
            <span className="day-task-counter">
              {doneTasks.length}/{dayTasks.length}
            </span>
          )}
          {!compact && <span className="day-month">{format(date, 'MMM yyyy')}</span>}
        </div>
        <button
          className="add-task-btn"
          onClick={e => { e.stopPropagation(); onCreate({ scheduledDate: date }); }}
          title="Add task"
        >
          <Plus size={14} />
        </button>
      </div>

      <div className="day-tasks">
        <DndContext
          sensors={sensors}
          collisionDetection={closestCenter}
          onDragStart={handleDragStart}
          onDragEnd={handleDragEnd}
        >
          <SortableContext items={activeTaskIds} strategy={verticalListSortingStrategy}>
            {activeTasks.map(task => (
              <TaskCard key={task.id} {...cardProps(task)} />
            ))}
          </SortableContext>

          <DragOverlay>
            {activeTask && (
              <TaskCard
                task={activeTask}
                allTasks={allTasks}
                currentDate={date}
                onUpdate={() => {}}
                onDelete={() => {}}
                onCreate={() => {}}
              />
            )}
          </DragOverlay>
        </DndContext>

        {activeTasks.length === 0 && (
          <div className="empty-day"><span>No tasks</span></div>
        )}
      </div>

      {/* Done section */}
      {doneTasks.length > 0 && (
        <div className="done-section">
          <div className="done-header">Done ({doneTasks.length})</div>
          {doneTasks.map(task => (
            <TaskCard key={task.id} {...cardProps(task)} />
          ))}
        </div>
      )}

      {/* Missed section — below done */}
      {missedTasks.length > 0 && (
        <div className="missed-section">
          <div className="missed-header">Missed ({missedTasks.length})</div>
          {missedTasks.map(task => (
            <TaskCard key={task.id} {...cardProps(task)} />
          ))}
        </div>
      )}
    </div>
  );
}


