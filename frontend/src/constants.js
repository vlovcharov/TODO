import { format, startOfWeek, endOfWeek, startOfMonth, endOfMonth, startOfYear, endOfYear, addDays, addWeeks, addMonths } from 'date-fns';

export const LEVELS = {
  Daily:    { label: 'Daily',     color: '#6366f1', bg: '#eef2ff', short: 'D' },
  Weekly:   { label: 'Weekly',    color: '#0ea5e9', bg: '#f0f9ff', short: 'W' },
  Monthly:  { label: 'Monthly',   color: '#10b981', bg: '#f0fdf4', short: 'M' },
  Yearly:   { label: 'Yearly',    color: '#f59e0b', bg: '#fffbeb', short: 'Y' },
  LifeGoal: { label: 'Life Goal', color: '#ec4899', bg: '#fdf4ff', short: 'L' },
};

export const PRIORITIES = {
  High:    { label: 'High',    color: '#ef4444', icon: '↑↑' },
  Average: { label: 'Average', color: '#f59e0b', icon: '↑'  },
  Low:     { label: 'Low',     color: '#94a3b8', icon: '↓'  },
};

export const VIEWS = ['Day', 'Week', 'Month', 'Year'];

// ── Recurrence bitmask ─────────────────────────────────────────────────────────
// Bit 0 = Mon, bit 1 = Tue, ... bit 6 = Sun  (matches backend RecurrenceDays)
export const DAY_BITS = {
  Mon: 1, Tue: 2, Wed: 4, Thu: 8, Fri: 16, Sat: 32, Sun: 64,
};

export const RECURRENCE_PRESETS = [
  { label: 'No recurrence',       mask: 0   },
  { label: 'Every day',           mask: 127 },
  { label: 'Working days',        mask: 31  },   // Mon–Fri
  { label: 'Mon, Wed, Fri',       mask: 1|4|16  },
  { label: 'Tue, Thu',            mask: 2|8  },
  { label: 'Weekends',            mask: 32|64 },
  { label: 'Custom…',             mask: -1  },   // sentinel — show day picker
];

export const DAY_LABELS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

/** Returns a short human-readable label for a recurrence mask */
export function recurrenceLabel(mask) {
  if (mask === 0)   return null;
  const preset = RECURRENCE_PRESETS.find(p => p.mask === mask && p.mask !== -1);
  if (preset) return preset.label;
  // Build "Mon Wed Fri" style label
  return DAY_LABELS.filter((_, i) => mask & (1 << i)).join(' ');
}

/** Is a task's recurrence mask active on a given JS Date? */
export function isRecurringActiveOnDate(task, date) {
  const mask = task.recurrenceMask ?? 0;
  if (mask === 0) return false;

  // Don't show before the task's creation/scheduled date
  const from = task.scheduledDate
    ? new Date(task.scheduledDate + 'T00:00:00')
    : new Date(task.createdAt);
  if (date < from) return false;

  // DayOfWeek mapping to our bit order (Mon=0…Sun=6)
  const jsDay = date.getDay(); // 0=Sun,1=Mon…6=Sat
  const bit = jsDay === 0 ? 64 : 1 << (jsDay - 1); // Sun→bit6, Mon→bit0…
  return (mask & bit) !== 0;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

export function formatDateParam(date) {
  return format(date, 'yyyy-MM-dd');
}

export function getViewRange(view, anchor) {
  switch (view) {
    case 'Day':   return { from: anchor, to: anchor };
    case 'Week':  return { from: startOfWeek(anchor, { weekStartsOn: 1 }), to: endOfWeek(anchor, { weekStartsOn: 1 }) };
    case 'Month': return { from: startOfMonth(anchor), to: endOfMonth(anchor) };
    case 'Year':  return { from: startOfYear(anchor),  to: endOfYear(anchor)  };
    default:      return { from: anchor, to: anchor };
  }
}

export function isCompletedOnDate(task, date) {
  const dateStr = formatDateParam(date);
  // All tasks now use completedDates (from TaskCompletions table)
  // IsCompleted is just a cache — use completedDates for accuracy on any given date
  return task.completedDates?.some(d =>
    (typeof d === 'string' ? d : formatDateParam(new Date(d + 'T00:00:00'))).substring(0, 10) === dateStr
  ) ?? false;
}

export function getNextPeriod(level, date) {
  switch (level) {
    case 'Daily':   return addDays(date, 1);
    case 'Weekly':  return addWeeks(date, 1);
    case 'Monthly': return addMonths(date, 1);
    case 'Yearly':  return addMonths(date, 12);
    default:        return date;
  }
}
