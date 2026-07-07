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
  formatShortDate,
  startOfWeek,
  toIsoDate,
} from '../format';
import { DaySummary, DraftResult, TimeOverview, WorkItem } from '../models';
import { ApiService } from '../services/api.service';
import { BookingService } from '../services/booking.service';
import { DevService } from '../services/dev.service';
import { RefreshService } from '../services/refresh.service';

/** Week review: per-day meters, drill-down into bookings, gap fixing (per-day booking + AI). */
@Component({
  selector: 'app-week-page',
  imports: [DraftReviewDialog, EditWorkItemDialog, LogTimeDialog],
  template: `
    <div class="page">
      <div class="toolbar">
        <button type="button" (click)="shiftWeek(-1)" title="Vorherige Woche">‹</button>
        <button type="button" (click)="goToday()">Heute</button>
        <button type="button" (click)="shiftWeek(1)" title="Nächste Woche">›</button>
        <span class="week-label">{{ weekLabel() }}</span>
        <span class="flex-spacer"></span>
        <button type="button" (click)="fillGaps()" [disabled]="aiBusy() || loading()">KI: Lücken füllen</button>
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
      } @else if (overview(); as ov) {
        <div class="table-scroll">
          <table class="data-table week-table">
            <thead>
              <tr>
                <th>Tag</th>
                <th class="num">Gebucht</th>
                <th class="meter-col">Fortschritt</th>
                <th class="num">Soll</th>
                <th>Lücke</th>
                <th class="num">Fokus</th>
              </tr>
            </thead>
            <tbody>
              @for (d of ov.days; track d.date) {
                <tr
                  class="day-row"
                  [class.today]="d.date === today"
                  [class.off-day]="!d.isWorkday"
                  (click)="toggle(d.date)"
                >
                  <td>
                    <span class="expand-caret">{{ isExpanded(d.date) ? '▾' : '▸' }}</span>
                    {{ dayLabel(d.date) }}
                  </td>
                  <td class="num">{{ clock(d.bookedMinutes) }}</td>
                  <td class="meter-col">
                    @if (d.isWorkday && d.targetMinutes > 0) {
                      <span class="meter">
                        <span
                          class="meter-fill"
                          [class.ok]="d.gapMinutes === 0"
                          [style.width.%]="percentOfTarget(d)"
                        ></span>
                      </span>
                    }
                  </td>
                  <td class="num muted">{{ clock(d.targetMinutes) }}</td>
                  <td>
                    @if (d.gapMinutes > 0) {
                      <span class="badge amber">Lücke {{ dur(d.gapMinutes) }}</span>
                    }
                  </td>
                  <td class="num">{{ score(d.fokusScore) }}</td>
                </tr>
                @if (isExpanded(d.date)) {
                  <tr class="detail-row">
                    <td colspan="6">
                      @if (d.items.length > 0) {
                        <div class="day-meta muted small">
                          {{ d.contextSwitches }} Kontextwechsel · Deep Work {{ percent(d.deepWorkShare) }}
                        </div>
                        <table class="items-table">
                          <tbody>
                            @for (item of d.items; track item.id) {
                              <tr>
                                <td class="nowrap"><span class="issue-id">{{ item.issueId }}</span></td>
                                <td class="summary-cell">{{ item.issueSummary }}</td>
                                <td class="num nowrap">{{ dur(item.minutes) }}</td>
                                <td class="nowrap">{{ item.typeName ?? '–' }}</td>
                                <td class="muted">{{ item.text ?? '' }}</td>
                                @if (dev.isSelf()) {
                                  <td class="nowrap row-actions always-visible">
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
                                        ×
                                      </button>
                                    }
                                  </td>
                                }
                              </tr>
                            }
                          </tbody>
                        </table>
                      } @else {
                        <span class="muted small">Keine Einträge.</span>
                      }
                      @if (dev.isSelf()) {
                        <div class="day-actions">
                          <button type="button" class="small" (click)="bookForDay(d.date, $event)">
                            Zeit buchen für diesen Tag
                          </button>
                        </div>
                      }
                    </td>
                  </tr>
                }
              }
            </tbody>
            <tfoot>
              <tr>
                <td>Total</td>
                <td class="num">{{ clock(ov.totalBookedMinutes) }}</td>
                <td></td>
                <td class="num muted">{{ clock(ov.totalTargetMinutes) }}</td>
                <td>
                  @if (ov.totalTargetMinutes - ov.totalBookedMinutes > 0) {
                    <span class="badge amber">Lücke {{ dur(ov.totalTargetMinutes - ov.totalBookedMinutes) }}</span>
                  }
                </td>
                <td class="num">Ø {{ score(ov.averageFokusScore) }}</td>
              </tr>
            </tfoot>
          </table>
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
  protected readonly dev = inject(DevService);

  readonly weekStart = signal(startOfWeek(new Date()));
  readonly overview = signal<TimeOverview | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly aiBusy = signal(false);
  readonly draftResult = signal<DraftResult | null>(null);
  readonly expanded = signal<ReadonlySet<string>>(new Set());
  readonly logDate = signal<string | null>(null);
  readonly logIssueId = signal('');
  readonly notice = signal<string | null>(null);
  readonly editItem = signal<WorkItem | null>(null);
  readonly confirmDeleteId = signal<string | null>(null);
  readonly deletingId = signal<string | null>(null);
  private deleteResetHandle: ReturnType<typeof setTimeout> | null = null;

  readonly today = toIsoDate(new Date());

  readonly weekLabel = computed(() => {
    const from = this.weekStart();
    const to = addDays(from, 6);
    return `${formatShortDate(toIsoDate(from))} – ${formatShortDate(toIsoDate(to))}`;
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

  bookForDay(date: string, event: Event): void {
    event.stopPropagation(); // don't collapse the row
    this.logIssueId.set('');
    this.logDate.set(date);
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

  percentOfTarget(day: DaySummary): number {
    return Math.min(100, (day.bookedMinutes / day.targetMinutes) * 100);
  }

  dayLabel(date: string): string {
    return formatDayLabel(date);
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
}
