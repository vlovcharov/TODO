import { useMemo, useState } from 'react';
import {
  startOfWeek, endOfWeek, startOfMonth, endOfMonth,
  eachDayOfInterval, isWeekend, isBefore, isAfter
} from 'date-fns';
import { ChevronDown, ChevronRight, TrendingUp } from 'lucide-react';
import { PRIORITIES, formatDateParam, isRecurringActiveOnDate } from '../constants';
import './StatsPanel.css';

const PRIORITY_ORDER = ['High', 'Average', 'Low'];

function buildStats(tasks, from, to) {
  const days = eachDayOfInterval({ start: from, end: to });

  // Collect every (task, date) instance that's visible in the period
  const instances = [];

  for (const task of tasks) {
    if (task.parentId) continue; // skip subtasks — count parents only

    if (task.recurrenceMask !== 0) {
      // Recurring: one instance per active day in the range
      for (const day of days) {
        if (isRecurringActiveOnDate(task, day)) {
          const dateStr = formatDateParam(day);
          const done = task.completedDates?.some(d =>
            (typeof d === 'string' ? d : formatDateParam(new Date(d))).substring(0, 10) === dateStr
          ) ?? false;
          instances.push({ task, done, date: day });
        }
      }
    } else {
      // Normal task: belongs in range if its scheduled date falls within
      const scheduled = new Date(task.scheduledDate + 'T00:00:00');
      if (scheduled >= from && scheduled <= to) {
        instances.push({ task, done: task.isCompleted, date: scheduled });
      }
    }
  }

  const total = instances.length;
  const done = instances.filter(i => i.done).length;
  const pending = total - done;

  // Priority breakdown for DONE tasks
  const byPriority = {};
  for (const p of PRIORITY_ORDER) {
    const doneCount = instances.filter(i => i.done && i.task.priority === p).length;
    const totalCount = instances.filter(i => i.task.priority === p).length;
    byPriority[p] = { done: doneCount, total: totalCount };
  }

  // Level breakdown for DONE tasks
  const byLevel = {};
  const levelKeys = ['Daily', 'Weekly', 'Monthly', 'Yearly', 'LifeGoal'];
  for (const l of levelKeys) {
    const doneCount = instances.filter(i => i.done && i.task.level === l).length;
    const totalCount = instances.filter(i => i.task.level === l).length;
    if (totalCount > 0) byLevel[l] = { done: doneCount, total: totalCount };
  }

  return { total, done, pending, byPriority, byLevel };
}

function StatBlock({ label, stats, accentColor }) {
  const [expanded, setExpanded] = useState(true);
  const pct = stats.total > 0 ? Math.round((stats.done / stats.total) * 100) : 0;

  return (
    <div className="stat-block">
      <button className="stat-block-header" onClick={() => setExpanded(e => !e)}>
        <span className="stat-block-label">{label}</span>
        {expanded ? <ChevronDown size={13} /> : <ChevronRight size={13} />}
      </button>

      {expanded && (
        <div className="stat-block-body">
          {/* Progress bar */}
          <div className="stat-progress-row">
            <div className="stat-progress-bar">
              <div
                className="stat-progress-fill"
                style={{ width: `${pct}%`, background: accentColor }}
              />
            </div>
            <span className="stat-pct">{pct}%</span>
          </div>

          {/* Summary counts */}
          <div className="stat-counts">
            <div className="stat-count-item">
              <span className="stat-count-num" style={{ color: accentColor }}>{stats.done}</span>
              <span className="stat-count-lbl">done</span>
            </div>
            <div className="stat-count-divider" />
            <div className="stat-count-item">
              <span className="stat-count-num">{stats.pending}</span>
              <span className="stat-count-lbl">left</span>
            </div>
            <div className="stat-count-divider" />
            <div className="stat-count-item">
              <span className="stat-count-num">{stats.total}</span>
              <span className="stat-count-lbl">total</span>
            </div>
          </div>

          {/* Priority breakdown */}
          {stats.total > 0 && (
            <div className="stat-breakdown">
              <div className="stat-breakdown-title">By priority</div>
              {PRIORITY_ORDER.map(p => {
                const { done: d, total: t } = stats.byPriority[p];
                if (t === 0) return null;
                const meta = PRIORITIES[p];
                return (
                  <div key={p} className="stat-breakdown-row">
                    <span className="stat-priority-icon" style={{ color: meta.color }}>
                      {meta.icon}
                    </span>
                    <span className="stat-breakdown-name">{p}</span>
                    <div className="stat-mini-bar">
                      <div
                        className="stat-mini-fill"
                        style={{
                          width: `${Math.round((d / t) * 100)}%`,
                          background: meta.color + 'bb'
                        }}
                      />
                    </div>
                    <span className="stat-breakdown-val">
                      {d}<span className="stat-breakdown-total">/{t}</span>
                    </span>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

export default function StatsPanel({ tasks, anchor }) {
  const [open, setOpen] = useState(true);

  const today = anchor || new Date();

  const weekFrom = startOfWeek(today, { weekStartsOn: 1 });
  const weekTo   = endOfWeek(today,   { weekStartsOn: 1 });
  const monthFrom = startOfMonth(today);
  const monthTo   = endOfMonth(today);

  const weekStats  = useMemo(() => buildStats(tasks, weekFrom, weekTo),  [tasks, weekFrom.getTime(), weekTo.getTime()]);
  const monthStats = useMemo(() => buildStats(tasks, monthFrom, monthTo), [tasks, monthFrom.getTime(), monthTo.getTime()]);

  return (
    <div className="stats-panel">
      <button className="stats-panel-toggle" onClick={() => setOpen(o => !o)}>
        <TrendingUp size={14} />
        <span>Statistics</span>
        {open ? <ChevronDown size={13} /> : <ChevronRight size={13} />}
      </button>

      {open && (
        <div className="stats-panel-body">
          <StatBlock
            label="This week"
            stats={weekStats}
            accentColor="#0ea5e9"
          />
          <StatBlock
            label="This month"
            stats={monthStats}
            accentColor="#10b981"
          />
        </div>
      )}
    </div>
  );
}
