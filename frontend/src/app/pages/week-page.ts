import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { DraftReviewDialog } from '../dialogs/draft-review-dialog';
import { EditWorkItemDialog } from '../dialogs/edit-work-item-dialog';
import { LogTimeDialog } from '../dialogs/log-time-dialog';
import {
  addDays,
  formatClock,
  formatDayLabel,
  formatDuration,
  formatScore,
  startOfWeek,
  toIsoDate,
} from '../format';
import { DayAbsence, DaySummary, DraftResult, TimeOverview, WorkItem } from '../models';
import { ApiService } from '../services/api.service';
import { BookingService } from '../services/booking.service';
import { DayStateService } from '../services/day-state.service';
import { DayTargetService } from '../services/day-target.service';
import { DevService } from '../services/dev.service';
import { RefreshService } from '../services/refresh.service';

/** Per-day row model, derived from the DaySummary plus personal absence + live target. */
interface DayRow {
  day: DaySummary;
  isToday: boolean;
  future: boolean;
  /** Effective target: personal absence factor applied, live for today, 0 on weekends. */
  target: number;
  gap: number;
  absence: DayAbsence;
  /** Full-day absence: meter empty + hatched, target 0, never a "Lücke". */
  hatched: boolean;
  pct: number;
  done: boolean;
  gapKind: 'absence' | 'done' | 'future' | 'gap' | 'none';
  gapLabel: string;
  focusColor: string;
}

const ABSENCE_OPTIONS: readonly { value: DayAbsence; label: string }[] = [
  { value: 'none', label: 'Keine' },
  { value: 'half', label: 'Halbtag' },
  { value: 'full', label: 'Ganzer Tag' },
];

/** Week review: per-day card rows with meters, drill-down into bookings, gap fixing (per-day booking + AI). */
@Component({
  selector: 'app-week-page',
  imports: [DraftReviewDialog, EditWorkItemDialog, LogTimeDialog],
  template: `
    <div class="page">
      <div class="toolbar">
        <button type="button" (click)="shiftWeek(-1)" title="Vorherige Woche">‹</button>
        <button type="button" (click)="goToday()">Heute</button>
        <button type="button" (click)="shiftWeek(1)" title="Nächste Woche">›</button>
        <h1 style="margin:0;font-size:1.2rem">Woche</h1>
        <span class="muted small nowrap">{{ weekLabel() }}</span>
        <span class="flex-spacer"></span>
        @if (overview(); as ov) {
          <span class="muted small nowrap">
            Total
            <span class="mono" style="font-weight:600;color:var(--text)">{{ clock(ov.totalBookedMinutes) }}</span>
            / {{ clock(weekTargetMinutes()) }}
          </span>
        }
        <button
          type="button"
          (click)="fillGaps()"
          [disabled]="aiBusy() || loading()"
          style="background:var(--accent-weak);border-color:var(--accent);color:var(--accent);font-weight:600"
        >
          ✦ KI: Lücken füllen
        </button>
        <button type="button" (click)="load(true)" [disabled]="loading()">Aktualisieren</button>
      </div>

      @if (aiBusy()) {
        <div class="banner info">
          <span class="spinner"></span> Claude denkt nach… das kann eine Minute dauern
        </div>
      }

      @if (error(); as err) {
        <div class="banner error">
          {{ err }}
          <button type="button" class="link" (click)="error.set(null)">schliessen</button>
        </div>
      }
      @if (notice(); as n) {
        <div class="banner success">
          {{ n }}
          <button type="button" class="link" (click)="notice.set(null)">schliessen</button>
        </div>
      }

      @if (loading()) {
        <div class="loading"><span class="spinner"></span> Woche laden…</div>
      } @else if (overview()) {
        <div style="display:flex;flex-direction:column;gap:0.5rem">
          @for (r of rows(); track r.day.date) {
            <div class="card flush" [style.border-color]="isExpanded(r.day.date) ? 'var(--border-strong)' : null">
              <button
                type="button"
                (click)="toggle(r.day.date)"
                style="display:grid;grid-template-columns:110px 76px 1fr 130px 100px 24px;gap:12px;align-items:center;width:100%;background:none;border:none;border-radius:0;padding:0.75rem 1rem;text-align:left;color:var(--text);cursor:pointer;font:inherit"
              >
                <span class="nowrap">
                  <span style="font-weight:600">{{ weekday(r.day.date) }}</span>
                  <span class="mono muted small">{{ dayDate(r.day.date) }}</span>
                  @if (r.isToday) {
                    <span class="small" style="color:var(--accent);font-weight:600">· Heute</span>
                  }
                </span>
                <span class="mono nowrap" style="font-weight:600">{{ clock(r.day.bookedMinutes) }}</span>
                <span class="meter" style="height:7px" [style.background]="r.hatched ? 'var(--hatch)' : null">
                  @if (!r.hatched && r.pct > 0) {
                    <span
                      class="meter-fill"
                      [class.ok]="r.done"
                      [class.warn]="!r.done"
                      [style.width.%]="r.pct"
                    ></span>
                  }
                </span>
                <span style="justify-self:start">
                  @if (r.gapKind !== 'none') {
                    <span
                      class="gap-pill nowrap"
                      [class.ok]="r.gapKind === 'done'"
                      [class.warn]="r.gapKind === 'gap'"
                      [style.background]="r.gapKind === 'absence' || r.gapKind === 'future' ? 'var(--surface-2)' : null"
                      [style.color]="r.gapKind === 'absence' || r.gapKind === 'future' ? 'var(--muted)' : null"
                    >
                      {{ r.gapLabel }}
                    </span>
                  }
                </span>
                <span class="muted small nowrap">
                  Fokus
                  <span class="mono" style="font-weight:600" [style.color]="r.focusColor">{{ score(r.day.fokusScore) }}</span>
                </span>
                <span class="muted small" style="text-align:center">{{ isExpanded(r.day.date) ? '▲' : '▼' }}</span>
              </button>

              @if (isExpanded(r.day.date)) {
                <div style="border-top:1px solid var(--border);padding:0.4rem 1rem 0.8rem">
                  @if (r.day.items.length > 0) {
                    <div class="day-meta muted small">
                      {{ r.day.contextSwitches }} Kontextwechsel
                      @if (switchPenalty(r.day) > 0) {
                        <span class="fokus-penalty">−{{ switchPenalty(r.day) }}</span>
                      }
                      · Deep Work {{ percent(r.day.deepWorkShare) }}
                      @if (deepPenalty(r.day) > 0) {
                        <span class="fokus-penalty">−{{ deepPenalty(r.day) }}</span>
                      }
                    </div>
                    @for (item of r.day.items; track item.id) {
                      <div style="display:flex;align-items:center;gap:0.6rem;padding:0.4rem 0;border-bottom:1px solid var(--border)">
                        <span class="issue-id">{{ item.issueId }}</span>
                        <span class="small" style="flex:1;min-width:0" [title]="item.issueSummary">
                          {{ item.text || item.issueSummary }}
                          @if (item.typeName) {
                            <span class="muted">· {{ item.typeName }}</span>
                          }
                        </span>
                        <span class="mono nowrap" style="font-weight:600">{{ dur(item.minutes) }}</span>
                        @if (dev.isSelf()) {
                          <span class="nowrap">
                            <button
                              type="button"
                              class="icon"
                              title="Buchung bearbeiten"
                              [disabled]="deletingId() !== null"
                              (click)="openEdit(item, $event)"
                            >
                              ✎
                            </button>
                            @if (confirmDeleteId() === item.id) {
                              <button
                                type="button"
                                class="icon danger"
                                title="Wirklich löschen?"
                                [disabled]="deletingId() !== null"
                                (click)="deleteItem(item, $event)"
                              >
                                Löschen?
                              </button>
                            } @else {
                              <button
                                type="button"
                                class="icon"
                                title="Buchung löschen"
                                [disabled]="deletingId() !== null"
                                (click)="armDelete(item, $event)"
                              >
                                ✕
                              </button>
                            }
                          </span>
                        }
                      </div>
                    }
                  } @else {
                    <div class="muted small" style="padding:0.4rem 0">Keine Einträge.</div>
                  }
                  @if (dev.isSelf()) {
                    <div style="display:flex;align-items:center;gap:0.5rem;margin-top:0.7rem;flex-wrap:wrap">
                      <button type="button" (click)="bookForDay(r, $event)">
                        + Nachbuchen auf {{ weekday(r.day.date) }} {{ dayDate(r.day.date) }}
                      </button>
                      <span class="muted small" style="margin-left:auto;font-weight:600">Absenz:</span>
                      @for (opt of absenceOptions; track opt.value) {
                        <button
                          type="button"
                          class="state-chip"
                          [class.active]="r.absence === opt.value"
                          (click)="setAbsence(r.day.date, opt.value, $event)"
                        >
                          {{ opt.label }}
                        </button>
                      }
                    </div>
                  }
                </div>
              }
            </div>
          }
        </div>
      }
    </div>

    @if (draftResult(); as result) {
      <app-draft-review-dialog
        [drafts]="result.drafts"
        [unmatched]="result.unmatched"
        (closed)="draftResult.set(null)"
      />
    }
    @if (logDate(); as date) {
      <app-log-time-dialog
        [issueId]="logIssueId()"
        [issueSummary]="''"
        [initialDate]="date"
        [initialMinutes]="logMinutes()"
        (closed)="logDate.set(null)"
      />
    }
    @if (editItem(); as item) {
      <app-edit-work-item-dialog [item]="item" (closed)="editItem.set(null)" />
    }
  `,
})
export class WeekPage {
  private readonly api = inject(ApiService);
  private readonly refresh = inject(RefreshService);
  private readonly booking = inject(BookingService);
  private readonly dayState = inject(DayStateService);
  private readonly dayTarget = inject(DayTargetService);
  protected readonly dev = inject(DevService);

  protected readonly absenceOptions = ABSENCE_OPTIONS;

  readonly weekStart = signal(startOfWeek(new Date()));
  readonly overview = signal<TimeOverview | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly aiBusy = signal(false);
  readonly draftResult = signal<DraftResult | null>(null);
  readonly expanded = signal<ReadonlySet<string>>(new Set());
  readonly logDate = signal<string | null>(null);
  readonly logIssueId = signal('');
  readonly logMinutes = signal(30);
  readonly notice = signal<string | null>(null);
  readonly editItem = signal<WorkItem | null>(null);
  readonly confirmDeleteId = signal<string | null>(null);
  readonly deletingId = signal<string | null>(null);
  private deleteResetHandle: ReturnType<typeof setTimeout> | null = null;

  readonly today = toIsoDate(new Date());

  /** "13. – 17. Juli 2026 · KW 29" (Mo–Fr range of the shown week). */
  readonly weekLabel = computed(() => {
    const from = this.weekStart();
    const to = addDays(from, 4);
    const month = (d: Date) => d.toLocaleDateString('de-CH', { month: 'long' });
    const sameMonth = from.getMonth() === to.getMonth() && from.getFullYear() === to.getFullYear();
    const range = sameMonth
      ? `${from.getDate()}. – ${to.getDate()}. ${month(to)} ${to.getFullYear()}`
      : `${from.getDate()}. ${month(from)} – ${to.getDate()}. ${month(to)} ${to.getFullYear()}`;
    return `${range} · KW ${isoWeekNumber(from)}`;
  });

  /** Week target from the day-target service (personal absences applied), weekends count 0. */
  readonly weekTargetMinutes = computed(() => {
    const ov = this.overview();
    if (!ov) {
      return 0;
    }
    return ov.days.reduce((sum, d) => sum + this.effectiveTarget(d), 0);
  });

  /** Mo–Fr rows, plus weekend rows only when they carry bookings. */
  readonly rows = computed<DayRow[]>(() => {
    const ov = this.overview();
    if (!ov) {
      return [];
    }
    return ov.days
      .filter((d) => d.isWorkday || d.bookedMinutes > 0 || d.items.length > 0)
      .map((d) => this.buildRow(d));
  });

  constructor() {
    effect(() => {
      this.refresh.worklogVersion();
      this.weekStart();
      this.dev.devParam();
      untracked(() => void this.load(false));
    });
  }

  shiftWeek(direction: number): void {
    this.weekStart.update((d) => addDays(d, direction * 7));
    this.expanded.set(new Set());
  }

  goToday(): void {
    this.weekStart.set(startOfWeek(new Date()));
    this.expanded.set(new Set());
  }

  toggle(date: string): void {
    this.expanded.update((set) => {
      const next = new Set(set);
      if (next.has(date)) {
        next.delete(date);
      } else {
        next.add(date);
      }
      return next;
    });
  }

  isExpanded(date: string): boolean {
    return this.expanded().has(date);
  }

  /** Retro booking for a day: pick-a-ticket dialog, prefilled with max(30, day gap). */
  bookForDay(row: DayRow, event: Event): void {
    event.stopPropagation(); // don't collapse the row
    this.logIssueId.set('');
    this.logMinutes.set(Math.max(30, row.gap));
    this.logDate.set(row.day.date);
  }

  setAbsence(date: string, absence: DayAbsence, event: Event): void {
    event.stopPropagation();
    this.dayState.update(date, { absence });
  }

  openEdit(item: WorkItem, event: Event): void {
    event.stopPropagation();
    this.editItem.set(item);
  }

  /** Two-step delete confirm, matching the topbar Verwerfen pattern (4s auto-disarm). */
  armDelete(item: WorkItem, event: Event): void {
    event.stopPropagation();
    this.confirmDeleteId.set(item.id);
    if (this.deleteResetHandle !== null) {
      clearTimeout(this.deleteResetHandle);
    }
    this.deleteResetHandle = setTimeout(() => this.confirmDeleteId.set(null), 4000);
  }

  async deleteItem(item: WorkItem, event: Event): Promise<void> {
    event.stopPropagation();
    this.confirmDeleteId.set(null);
    this.deletingId.set(item.id);
    this.error.set(null);
    try {
      await this.booking.remove(item);
      this.notice.set(`Buchung ${item.issueId} · ${formatDuration(item.minutes)} gelöscht`);
      setTimeout(() => this.notice.set(null), 5000);
    } catch (err) {
      this.error.set((err as Error).message);
      this.refresh.worklogChanged(); // stale row (e.g. already deleted) disappears anyway
    } finally {
      this.deletingId.set(null);
    }
  }

  async load(refresh: boolean): Promise<void> {
    const from = toIsoDate(this.weekStart());
    const to = toIsoDate(addDays(this.weekStart(), 6));
    void this.dayState.loadRange(from, to); // absences for the shown week
    if (!this.overview()) {
      this.loading.set(true);
    }
    this.error.set(null);
    try {
      this.overview.set(await this.api.getOverview(from, to, refresh, this.dev.devParam()));
    } catch (err) {
      this.error.set((err as Error).message);
    } finally {
      this.loading.set(false);
    }
  }

  async fillGaps(): Promise<void> {
    const from = toIsoDate(this.weekStart());
    const to = toIsoDate(addDays(this.weekStart(), 6));
    this.aiBusy.set(true);
    this.error.set(null);
    try {
      this.draftResult.set(await this.api.aiGapfills(from, to, this.dev.devParam()));
    } catch (err) {
      this.error.set((err as Error).message);
    } finally {
      this.aiBusy.set(false);
    }
  }

  /**
   * Row logic from the design mockup: the day target comes from DayTargetService
   * (personal absence factor, live presence-aware target for today) — not from the
   * backend DaySummary.targetMinutes. A full-day absence has target 0, never a "Lücke".
   */
  private buildRow(d: DaySummary): DayRow {
    const isToday = d.date === this.today;
    const future = d.date > this.today;
    const absence = this.dayState.stateFor(d.date).absence;
    const target = this.effectiveTarget(d);
    const gap = target - d.bookedMinutes;
    const hatched = d.isWorkday && absence === 'full';
    const done = !hatched && target > 0 && gap <= 0;
    const pct = target > 0 ? Math.min(100, (d.bookedMinutes / target) * 100) : 0;
    let gapKind: DayRow['gapKind'];
    let gapLabel: string;
    if (!d.isWorkday) {
      gapKind = 'none';
      gapLabel = '';
    } else if (hatched) {
      gapKind = 'absence';
      gapLabel = 'Absenz';
    } else if (done) {
      gapKind = 'done';
      gapLabel = 'Ziel erreicht';
    } else if (future) {
      gapKind = 'future';
      gapLabel = `offen · ${formatClock(gap)}`;
    } else {
      gapKind = 'gap';
      gapLabel = `Lücke ${formatClock(gap)}`;
    }
    const focusColor =
      d.fokusScore === null
        ? 'var(--muted-2)'
        : d.fokusScore >= 75
          ? 'var(--ok)'
          : d.fokusScore >= 60
            ? 'var(--amber)'
            : 'var(--red)';
    return { day: d, isToday, future, target, gap, absence, hatched, pct, done, gapKind, gapLabel, focusColor };
  }

  /** Personal-absence-aware day target; weekends have no target. */
  private effectiveTarget(d: DaySummary): number {
    return d.isWorkday ? this.dayTarget.targetFor(d.date, d.date === this.today) : 0;
  }

  weekday(date: string): string {
    return formatDayLabel(date).split(' ')[0];
  }

  dayDate(date: string): string {
    return formatDayLabel(date).split(' ')[1] ?? '';
  }

  clock(minutes: number): string {
    return formatClock(minutes);
  }

  dur(minutes: number): string {
    return formatDuration(minutes);
  }

  score(value: number | null): string {
    return formatScore(value);
  }

  percent(share: number): string {
    return `${Math.round(share * 100)}%`;
  }

  // Fokus-Score deductions — same constants as MetricsCalculator.FokusScore.
  switchPenalty(day: DaySummary): number {
    return 12 * Math.max(0, day.contextSwitches - 2);
  }

  deepPenalty(day: DaySummary): number {
    return day.bookedMinutes === 0 ? 0 : Math.round(30 * (1 - day.deepWorkShare));
  }
}

/** ISO-8601 week number (weeks start Monday; week 1 contains Jan 4). */
function isoWeekNumber(d: Date): number {
  const date = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  date.setDate(date.getDate() + 3 - ((date.getDay() + 6) % 7));
  const week1 = new Date(date.getFullYear(), 0, 4);
  return (
    1 +
    Math.round(
      ((date.getTime() - week1.getTime()) / 86_400_000 - 3 + ((week1.getDay() + 6) % 7)) / 7,
    )
  );
}
