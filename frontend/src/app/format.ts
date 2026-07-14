/** Formats minutes as a duration, e.g. 465 -> "7h 45m", 45 -> "45m", 0 -> "0m". */
export function formatDuration(minutes: number): string {
  const total = Math.round(minutes);
  const h = Math.floor(Math.abs(total) / 60);
  const m = Math.abs(total) % 60;
  const sign = total < 0 ? '-' : '';
  if (h === 0) {
    return `${sign}${m}m`;
  }
  if (m === 0) {
    return `${sign}${h}h`;
  }
  return `${sign}${h}h ${m}m`;
}

/** Formats minutes as a clock-style value, e.g. 465 -> "7:45". */
export function formatClock(minutes: number): string {
  const total = Math.round(minutes);
  const h = Math.floor(Math.abs(total) / 60);
  const m = Math.abs(total) % 60;
  return `${total < 0 ? '-' : ''}${h}:${String(m).padStart(2, '0')}`;
}

/** Formats elapsed seconds as "hh:mm:ss", e.g. 5025 -> "01:23:45". */
export function formatElapsed(totalSeconds: number): string {
  const s = Math.max(0, Math.floor(totalSeconds));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = s % 60;
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}:${String(sec).padStart(2, '0')}`;
}

/** Relative time for an ISO datetime, e.g. "3d ago". */
export function relativeTime(iso: string): string {
  const then = Date.parse(iso);
  if (Number.isNaN(then)) {
    return '';
  }
  const minutes = Math.floor((Date.now() - then) / 60_000);
  if (minutes < 1) {
    return 'gerade eben';
  }
  if (minutes < 60) {
    return `vor ${minutes}m`;
  }
  const hours = Math.floor(minutes / 60);
  if (hours < 24) {
    return `vor ${hours}h`;
  }
  const days = Math.floor(hours / 24);
  if (days < 7) {
    return `vor ${days}d`;
  }
  const weeks = Math.floor(days / 7);
  if (days < 30) {
    return `vor ${weeks}w`;
  }
  const months = Math.floor(days / 30);
  if (months < 12) {
    return `vor ${months}mo`;
  }
  return `vor ${Math.floor(days / 365)}y`;
}

/**
 * Parses a duration string into minutes. Accepts "1h 30m", "90m", "1.5h" and
 * plain minutes such as "90". Returns null for unparsable or non-positive input.
 */
export function parseDuration(input: string): number | null {
  const s = input.trim().toLowerCase().replace(',', '.');
  if (!s) {
    return null;
  }
  // Plain number: interpreted as minutes.
  if (/^\d+(?:\.\d+)?$/.test(s)) {
    const minutes = Math.round(parseFloat(s));
    return minutes > 0 ? minutes : null;
  }
  const match = s.match(/^(?:(\d+(?:\.\d+)?)\s*h)?\s*(?:(\d+(?:\.\d+)?)\s*m(?:in)?)?$/);
  if (!match || (match[1] === undefined && match[2] === undefined)) {
    return null;
  }
  let minutes = 0;
  if (match[1] !== undefined) {
    minutes += parseFloat(match[1]) * 60;
  }
  if (match[2] !== undefined) {
    minutes += parseFloat(match[2]);
  }
  minutes = Math.round(minutes);
  return minutes > 0 ? minutes : null;
}

/** Parses a wall-clock string ("7:45", "07:45", "7.45") into minutes since midnight, or null. */
export function parseClock(input: string): number | null {
  const match = (input ?? '').trim().match(/^(\d{1,2})[:.](\d{2})$/);
  if (!match) {
    return null;
  }
  const h = parseInt(match[1], 10);
  const m = parseInt(match[2], 10);
  return h < 24 && m < 60 ? h * 60 + m : null;
}

/** Formats minutes since midnight as "H:MM" wall-clock (e.g. 465 -> "7:45"). */
export function formatWallClock(minutesOfDay: number): string {
  const total = Math.max(0, Math.round(minutesOfDay));
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')}`;
}

/** Formats a Date as "yyyy-MM-dd" in local time. */
export function toIsoDate(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

export function addDays(d: Date, n: number): Date {
  const r = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  r.setDate(r.getDate() + n);
  return r;
}

/** Monday-start week. */
export function startOfWeek(d: Date): Date {
  const r = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  const offset = (r.getDay() + 6) % 7;
  r.setDate(r.getDate() - offset);
  return r;
}

const WEEKDAYS = ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'];

/** "Mo 06.07." for a "yyyy-MM-dd" string. */
export function formatDayLabel(isoDate: string): string {
  const d = new Date(`${isoDate}T00:00:00`);
  if (Number.isNaN(d.getTime())) {
    return isoDate;
  }
  return `${WEEKDAYS[d.getDay()]} ${String(d.getDate()).padStart(2, '0')}.${String(d.getMonth() + 1).padStart(2, '0')}.`;
}

/** "06.07.2026" for a "yyyy-MM-dd" string. */
export function formatShortDate(isoDate: string): string {
  const d = new Date(`${isoDate}T00:00:00`);
  if (Number.isNaN(d.getTime())) {
    return isoDate;
  }
  return `${String(d.getDate()).padStart(2, '0')}.${String(d.getMonth() + 1).padStart(2, '0')}.${d.getFullYear()}`;
}

/** Fokus score with at most one decimal, or "–" when null. */
export function formatScore(score: number | null): string {
  if (score === null || score === undefined) {
    return '–';
  }
  return String(Math.round(score * 10) / 10);
}
