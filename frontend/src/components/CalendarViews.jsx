import { eachDayOfInterval, eachWeekOfInterval, eachMonthOfInterval, startOfWeek, endOfWeek, startOfMonth, endOfMonth, format, isSameMonth, isToday } from 'date-fns';
import DayColumn from './DayColumn';
import EpicsPanel from './EpicsPanel';
import DayPlanner from './DayPlanner';
import { formatDateParam, LEVELS, isCompletedOnDate, isRecurringActiveOnDate } from '../constants';
import { tasksApi } from '../api';

// ─── DAY VIEW ──────────────────────────────────────────────────────────────
export function DayView({ anchor, tasks, epics, onUpdate, onDelete, onCreate, onEpicsChange }) {
  return (
    <div className="view-day">
      <DayColumn
        date={anchor}
        allTasks={tasks}
        epics={epics}
        onUpdate={onUpdate}
        onDelete={onDelete}
        onCreate={onCreate}
      />
      <EpicsPanel
        epics={epics}
        tasks={tasks}
        currentDate={anchor}
        onEpicsChange={onEpicsChange}
        onTasksChange={() => onUpdate(null)}
      />
      <DayPlanner
        date={anchor}
        tasks={tasks}
      />
    </div>
  );
}

// ─── WEEK VIEW ─────────────────────────────────────────────────────────────
export function WeekView({ anchor, tasks, onUpdate, onDelete, onCreate }) {
  const weekStart = startOfWeek(anchor, { weekStartsOn: 1 });
  const weekEnd = endOfWeek(anchor, { weekStartsOn: 1 });
  const days = eachDayOfInterval({ start: weekStart, end: weekEnd });

  return (
    <div className="view-week">
      {days.map(day => (
        <DayColumn
          key={formatDateParam(day)}
          date={day}
          allTasks={tasks}
          onUpdate={onUpdate}
          onDelete={onDelete}
          onCreate={onCreate}
          compact
        />
      ))}
    </div>
  );
}

// ─── MONTH VIEW ────────────────────────────────────────────────────────────
export function MonthView({ anchor, tasks, onUpdate, onDelete, onCreate }) {
  const monthStart = startOfMonth(anchor);
  const monthEnd = endOfMonth(anchor);
  const weeks = eachWeekOfInterval(
    { start: monthStart, end: monthEnd },
    { weekStartsOn: 1 }
  );

  return (
    <div className="view-month">
      <div className="month-header-row">
        {['Mon','Tue','Wed','Thu','Fri','Sat','Sun'].map(d => (
          <div key={d} className="month-col-header">{d}</div>
        ))}
      </div>
      {weeks.map(weekStart => {
        const days = eachDayOfInterval({
          start: weekStart,
          end: endOfWeek(weekStart, { weekStartsOn: 1 })
        });
        return (
          <div key={formatDateParam(weekStart)} className="month-week-row">
            {days.map(day => {
              const dateStr = formatDateParam(day);
              const dayTasks = tasks.filter(t => {
                if (t.parentId) return false;
                if ((t.recurrenceMask ?? 0) !== 0) return isRecurringActiveOnDate(t, day);
                return t.scheduledDate === dateStr;
              });
              const isCurrentMonth = isSameMonth(day, anchor);
              const todayClass = isToday(day) ? 'today' : '';

              return (
                <div
                  key={dateStr}
                  className={`month-cell ${isCurrentMonth ? '' : 'other-month'} ${todayClass}`}
                  onClick={() => onCreate({ scheduledDate: day })}
                >
                  <div className="month-cell-date">{format(day, 'd')}</div>
                  <div className="month-cell-tasks">
                    {dayTasks.slice(0, 3).map(t => {
                      const level = LEVELS[t.level];
                      const done = isCompletedOnDate(t, day);
                      return (
                        <div
                          key={t.id}
                          className={`month-task-pill ${done ? 'done' : ''}`}
                          style={{ background: level.bg, color: level.color, borderColor: level.color + '40' }}
                          title={t.title}
                          onClick={e => { e.stopPropagation(); }}
                        >
                          {t.title}
                        </div>
                      );
                    })}
                    {dayTasks.length > 3 && (
                      <div className="month-task-more">+{dayTasks.length - 3} more</div>
                    )}
                  </div>
                </div>
              );
            })}
          </div>
        );
      })}
    </div>
  );
}

// ─── YEAR VIEW ─────────────────────────────────────────────────────────────
export function YearView({ anchor, tasks, onUpdate, onDelete, onCreate, onNavigate }) {
  const months = eachMonthOfInterval({
    start: new Date(anchor.getFullYear(), 0, 1),
    end: new Date(anchor.getFullYear(), 11, 31),
  });

  return (
    <div className="view-year">
      {months.map(monthDate => {
        const monthStart = startOfMonth(monthDate);
        const monthEnd = endOfMonth(monthDate);
        const monthStr = format(monthDate, 'yyyy-MM');

        const monthTasks = tasks.filter(t => {
          if (t.parentId) return false;
          if (t.recurrenceMask ?? 0) return true;
          return t.scheduledDate?.startsWith(monthStr);
        });

        return (
          <div
            key={monthStr}
            className="year-month-cell"
            onClick={() => onNavigate && onNavigate('Month', monthDate)}
          >
            <div className="year-month-name">{format(monthDate, 'MMM')}</div>
            <div className="year-month-count">{monthTasks.length}</div>
            <div className="year-month-bar">
              {Object.entries(LEVELS).map(([lvl, meta]) => {
                const cnt = monthTasks.filter(t => t.level === lvl).length;
                if (!cnt) return null;
                return (
                  <div
                    key={lvl}
                    className="year-level-dot"
                    style={{ background: meta.color }}
                    title={`${cnt} ${meta.label}`}
                  />
                );
              })}
            </div>
          </div>
        );
      })}
    </div>
  );
}
