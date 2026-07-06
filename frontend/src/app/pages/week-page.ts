import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { DraftReviewDialog } from '../dialogs/draft-review-dialog';
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
import { BookingPreset, DraftResult, TimeOverview } from '../models';
import { ApiService } from '../services/api.service';
import { DevService } from '../services/dev.service';
import { RefreshService } from '../services/refresh.service';

@Component({
  selector: 'app-week-page',
  imports: [DraftReviewDialog],
  template: `
    <div class="page">
      <div class="toolbar">
        <button type="button" (click)="shiftWeek(-1)" title="Previous week">‹</button>
        <button type="button" (click)="goToday()">Today</button>
        <button type="button" (click)="shiftWeek(1)" title="Next week">›</button>
        <span class="week-label">{{ weekLabel() }}</span>
        <span class="flex-spacer"></span>
        <button type="button" (click)="fillGaps()" [disabled]="aiBusy() || loading()">AI: fill gaps</button>
        <button type="button" (click)="load(true)" [disabled]="loading()">Refresh</button>
      </div>

      @if (dev.isSelf() && presets().length > 0) {
        <div class="presets-strip">
          <span class="muted small">Presets:</span>
          @for (p of presets(); track p.id) {
            <span class="preset-chip" [class.busy]="bookingPresetId() === p.id">
              <button
                type="button"
                class="preset-book"
                [disabled]="bookingPresetId() !== null"
                (click)="bookPreset(p)"
                [title]="p.issueId + ' — ' + p.issueSummary + ' · books ' + dur(p.minutes) + ' for today'"
              >
                {{ p.name }} · {{ dur(p.minutes) }}
              </button>
              <button
                type="button"
                class="preset-delete"
                title="Delete preset"
                [disabled]="bookingPresetId() !== null"
                (click)="deletePreset(p)"
              >
                ×
              </button>
            </span>
          }
          <span class="muted small">(click to book for today — save new ones from the Log time dialog)</span>
        </div>
      }

      @if (aiBusy()) {
        <div class="banner info">
          <span class="spinner"></span> Claude is thinking… this can take a minute
        </div>
      }

      @if (error(); as err) {
        <div class="banner error">
          {{ err }}
          <button type="button" class="link" (click)="error.set(null)">dismiss</button>
        </div>
      }

      @if (loading()) {
        <div class="loading"><span class="spinner"></span> Loading week…</div>
      } @else if (overview(); as ov) {
        <div class="table-scroll">
          <table class="data-table week-table">
            <thead>
              <tr>
                <th>Day</th>
                <th class="num">Booked</th>
                <th class="num">Target</th>
                <th>Gap</th>
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
                  <td class="num muted">{{ clock(d.targetMinutes) }}</td>
                  <td>
                    @if (d.gapMinutes > 0) {
                      <span class="badge amber">gap {{ dur(d.gapMinutes) }}</span>
                    }
                  </td>
                  <td class="num">{{ score(d.fokusScore) }}</td>
                </tr>
                @if (isExpanded(d.date)) {
                  <tr class="detail-row">
                    <td colspan="5">
                      @if (d.items.length > 0) {
                        <div class="day-meta muted small">
                          {{ d.contextSwitches }} context switch{{ d.contextSwitches === 1 ? '' : 'es' }} ·
                          deep work {{ percent(d.deepWorkShare) }}
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
                              </tr>
                            }
                          </tbody>
                        </table>
                      } @else {
                        <span class="muted small">No entries.</span>
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
                <td class="num muted">{{ clock(ov.totalTargetMinutes) }}</td>
                <td>
                  @if (ov.totalTargetMinutes - ov.totalBookedMinutes > 0) {
                    <span class="badge amber">gap {{ dur(ov.totalTargetMinutes - ov.totalBookedMinutes) }}</span>
                  }
                </td>
                <td class="num">avg {{ score(ov.averageFokusScore) }}</td>
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
  `,
})
export class WeekPage {
  private readonly api = inject(ApiService);
  private readonly refresh = inject(RefreshService);
  protected readonly dev = inject(DevService);

  readonly weekStart = signal(startOfWeek(new Date()));
  readonly overview = signal<TimeOverview | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly aiBusy = signal(false);
  readonly draftResult = signal<DraftResult | null>(null);
  readonly expanded = signal<ReadonlySet<string>>(new Set());
  readonly presets = signal<BookingPreset[]>([]);
  readonly bookingPresetId = signal<string | null>(null);

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
      untracked(() => {
        void this.load(false);
        void this.loadPresets(); // picks up presets saved via the Log time dialog
      });
    });
  }

  async loadPresets(): Promise<void> {
    try {
      this.presets.set(await this.api.getPresets());
    } catch {
      this.presets.set([]); // strip simply stays hidden
    }
  }

  async bookPreset(preset: BookingPreset): Promise<void> {
    this.bookingPresetId.set(preset.id);
    this.error.set(null);
    try {
      await this.api.createWorklog({
        issueId: preset.issueId,
        date: this.today,
        minutes: preset.minutes,
        typeId: preset.typeId,
        text: preset.comment,
      });
      this.refresh.worklogChanged();
    } catch (err) {
      this.error.set((err as Error).message);
    } finally {
      this.bookingPresetId.set(null);
    }
  }

  async deletePreset(preset: BookingPreset): Promise<void> {
    try {
      await this.api.deletePreset(preset.id);
      await this.loadPresets();
    } catch (err) {
      this.error.set((err as Error).message);
    }
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

  async load(refresh: boolean): Promise<void> {
    const from = toIsoDate(this.weekStart());
    const to = toIsoDate(addDays(this.weekStart(), 6));
    this.loading.set(true);
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
