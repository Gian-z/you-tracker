import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DraftReviewDialog } from '../dialogs/draft-review-dialog';
import { LogTimeDialog } from '../dialogs/log-time-dialog';
import { addDays, formatClock, formatDuration, formatScore, startOfWeek, toIsoDate } from '../format';
import { BookingPreset, DraftResult, TaskListItem, TimeOverview, TriageResult } from '../models';
import { ApiService } from '../services/api.service';
import { DevService } from '../services/dev.service';
import { RefreshService } from '../services/refresh.service';
import { TimerService } from '../services/timer.service';

interface StateCount {
  state: string;
  count: number;
}

type AiAction = 'draft' | 'gaps' | 'summary' | 'triage';

/** Single-page overview: KPIs, my sprint tasks, status distribution, sprint pool, today's bookings, AI. */
@Component({
  selector: 'app-dashboard-page',
  imports: [FormsModule, DraftReviewDialog, LogTimeDialog],
  template: `
    <div class="page dashboard">
      @if (error(); as err) {
        <div class="banner error">
          {{ err }}
          <button type="button" class="link" (click)="error.set(null)">dismiss</button>
        </div>
      }

      <!-- KPI stat tiles: hero number, caption; amber only when a gap exists (with text). -->
      <div class="tiles">
        <div class="tile">
          <div class="tile-caption">Today</div>
          <div class="tile-value">{{ clock(todayBooked()) }}</div>
          <div class="tile-sub muted">
            of {{ clock(targetToday()) }}
            @if (todayGap() > 0) {
              <span class="badge amber">gap {{ dur(todayGap()) }}</span>
            }
          </div>
        </div>
        <div class="tile">
          <div class="tile-caption">Week</div>
          <div class="tile-value">{{ clock(week()?.totalBookedMinutes ?? 0) }}</div>
          <div class="tile-sub muted">of {{ clock(week()?.totalTargetMinutes ?? 0) }}</div>
        </div>
        <div class="tile">
          <div class="tile-caption">Fokus-Score</div>
          <div class="tile-value">{{ score(week()?.averageFokusScore ?? null) }}</div>
          <div class="tile-sub muted">week average</div>
        </div>
        <div class="tile">
          <div class="tile-caption">My tickets</div>
          <div class="tile-value">{{ issues().length }}</div>
          <div class="tile-sub muted">from your query</div>
        </div>
      </div>

      <div class="dash-grid">
        <!-- My sprint tasks -->
        <section class="card">
          <h2>My tickets <button type="button" class="link small" (click)="load(true)">refresh</button></h2>
          @if (loading()) {
            <div class="loading"><span class="spinner"></span> Loading…</div>
          } @else {
            <table class="data-table compact">
              <tbody>
                @for (t of issues(); track t.issueId) {
                  <tr>
                    <td class="nowrap"><a [href]="t.webUrl" target="_blank" rel="noopener">{{ t.issueId }}</a></td>
                    <td class="nowrap">{{ t.state ?? '–' }}</td>
                    <td class="summary-cell">{{ t.summary }}</td>
                    <td class="nowrap row-actions">
                      @if (dev.isSelf()) {
                        <button type="button" class="icon" title="Start timer" (click)="start(t)">▶</button>
                        <button type="button" class="icon" title="Log time" (click)="logIssue.set(t)">✎</button>
                      }
                    </td>
                  </tr>
                } @empty {
                  <tr><td class="muted empty-cell">No tickets.</td></tr>
                }
              </tbody>
            </table>
          }
        </section>

        <!-- Status distribution: one series, single hue; label = identity, length = magnitude. -->
        <section class="card">
          <h2>Status</h2>
          @for (s of stateCounts(); track s.state) {
            <div class="dist-row">
              <span class="dist-label">{{ s.state }}</span>
              <span class="dist-track">
                <span class="dist-bar" [style.width.%]="(s.count / maxStateCount()) * 100"></span>
              </span>
              <span class="dist-count">{{ s.count }}</span>
            </div>
          } @empty {
            <div class="muted small">No data.</div>
          }

          <h2 class="mt">Unclaimed sprint tasks</h2>
          @for (t of pool(); track t.issueId) {
            <div class="pool-row">
              <a [href]="t.webUrl" target="_blank" rel="noopener" class="issue-id">{{ t.issueId }}</a>
              <span class="summary-cell small">{{ t.summary }}</span>
              <span class="muted small nowrap">{{ t.estimate ?? '' }}</span>
            </div>
          } @empty {
            <div class="muted small">None (or no pool query configured).</div>
          }
        </section>

        <!-- Today's bookings + presets -->
        <section class="card">
          <h2>Today's bookings</h2>
          @if (todayItems().length > 0) {
            <table class="data-table compact">
              <tbody>
                @for (item of todayItems(); track item.id) {
                  <tr>
                    <td class="nowrap"><span class="issue-id">{{ item.issueId }}</span></td>
                    <td class="summary-cell">{{ item.text || item.issueSummary }}</td>
                    <td class="num nowrap">{{ dur(item.minutes) }}</td>
                  </tr>
                }
              </tbody>
            </table>
          } @else {
            <div class="muted small">Nothing booked today yet.</div>
          }
          @if (dev.isSelf() && presets().length > 0) {
            <div class="presets-strip">
              @for (p of presets(); track p.id) {
                <span class="preset-chip" [class.busy]="bookingPresetId() === p.id">
                  <button
                    type="button"
                    class="preset-book"
                    [disabled]="bookingPresetId() !== null"
                    (click)="bookPreset(p)"
                    [title]="p.issueId + ' · books ' + dur(p.minutes) + ' for today'"
                  >
                    {{ p.name }} · {{ dur(p.minutes) }}
                  </button>
                </span>
              }
            </div>
          }
        </section>

        <!-- AI panel -->
        <section class="card">
          <h2>Assistant</h2>
          <textarea
            rows="2"
            [(ngModel)]="freeText"
            placeholder="Describe your day… ('morning XBOX-587 testing, afternoon reviews')"
            [disabled]="aiBusy() !== null"
          ></textarea>
          <div class="toolbar wrap">
            <button type="button" (click)="ai('draft')" [disabled]="aiBusy() !== null || !freeText().trim()">Draft work log</button>
            <button type="button" (click)="ai('gaps')" [disabled]="aiBusy() !== null">Fill week gaps</button>
            <button type="button" (click)="ai('summary')" [disabled]="aiBusy() !== null">Summarize day</button>
            <button type="button" (click)="ai('triage')" [disabled]="aiBusy() !== null">Triage</button>
          </div>
          @if (aiBusy(); as action) {
            <div class="banner info"><span class="spinner"></span> Claude is thinking ({{ action }})… this can take a minute</div>
          }
          @if (summaryText(); as text) {
            <div class="summary-output">{{ text }}</div>
          }
          @if (triageResult(); as t) {
            @if (t.focusSuggestion) {
              <div class="focus-suggestion"><strong>Focus:</strong> {{ t.focusSuggestion }}</div>
            }
            <ol class="triage-list compact-list">
              @for (entry of t.ranked.slice(0, 5); track entry.issueId) {
                <li><span class="rank">#{{ entry.rank }}</span> <span class="issue-id">{{ entry.issueId }}</span> {{ entry.summary }}</li>
              }
            </ol>
            @if (t.sprintSuggestions.length > 0) {
              <h3 class="sprint-suggestions-title">Suggested from sprint</h3>
              <ul class="compact-list">
                @for (entry of t.sprintSuggestions; track entry.issueId) {
                  <li>
                    <span class="badge green">pick up?</span>
                    <span class="issue-id">{{ entry.issueId }}</span> {{ entry.summary }}
                    <div class="muted small">{{ entry.reasons.join(' · ') }}</div>
                  </li>
                }
              </ul>
            }
          }
        </section>
      </div>
    </div>

    @if (logIssue(); as issue) {
      <app-log-time-dialog [issueId]="issue.issueId" [issueSummary]="issue.summary" (closed)="logIssue.set(null)" />
    }
    @if (draftResult(); as result) {
      <app-draft-review-dialog [drafts]="result.drafts" [unmatched]="result.unmatched" (closed)="draftResult.set(null)" />
    }
  `,
})
export class DashboardPage {
  private readonly api = inject(ApiService);
  private readonly refresh = inject(RefreshService);
  private readonly timer = inject(TimerService);
  protected readonly dev = inject(DevService);

  readonly issues = signal<TaskListItem[]>([]);
  readonly pool = signal<TaskListItem[]>([]);
  readonly week = signal<TimeOverview | null>(null);
  readonly presets = signal<BookingPreset[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly logIssue = signal<TaskListItem | null>(null);
  readonly bookingPresetId = signal<string | null>(null);
  readonly aiBusy = signal<AiAction | null>(null);
  readonly freeText = signal('');
  readonly draftResult = signal<DraftResult | null>(null);
  readonly summaryText = signal<string | null>(null);
  readonly triageResult = signal<TriageResult | null>(null);

  readonly today = toIsoDate(new Date());

  readonly todayItems = computed(
    () => this.week()?.days.find((d) => d.date === this.today)?.items ?? [],
  );
  readonly todayBooked = computed(() =>
    this.todayItems().reduce((sum, item) => sum + item.minutes, 0),
  );
  readonly targetToday = computed(
    () => this.week()?.days.find((d) => d.date === this.today)?.targetMinutes ?? 0,
  );
  readonly todayGap = computed(() => Math.max(0, this.targetToday() - this.todayBooked()));

  readonly stateCounts = computed<StateCount[]>(() => {
    const counts = new Map<string, number>();
    for (const issue of this.issues()) {
      const state = issue.state ?? 'No state';
      counts.set(state, (counts.get(state) ?? 0) + 1);
    }
    return [...counts.entries()]
      .map(([state, count]) => ({ state, count }))
      .sort((a, b) => b.count - a.count);
  });
  readonly maxStateCount = computed(() =>
    Math.max(1, ...this.stateCounts().map((s) => s.count)),
  );

  constructor() {
    effect(() => {
      this.refresh.worklogVersion();
      this.dev.devParam();
      untracked(() => void this.load(false));
    });
  }

  async load(refresh: boolean): Promise<void> {
    const devParam = this.dev.devParam();
    const monday = startOfWeek(new Date());
    const from = toIsoDate(monday);
    const to = toIsoDate(addDays(monday, 6));
    this.loading.set(true);
    this.error.set(null);
    try {
      const [issues, weekOverview, poolTasks, presets] = await Promise.all([
        this.api.getIssues(refresh, devParam),
        this.api.getOverview(from, to, refresh, devParam),
        this.api.getSprintPool(refresh, devParam),
        this.api.getPresets().catch(() => [] as BookingPreset[]),
      ]);
      this.issues.set(issues);
      this.week.set(weekOverview);
      this.pool.set(poolTasks);
      this.presets.set(presets);
    } catch (err) {
      this.error.set((err as Error).message);
    } finally {
      this.loading.set(false);
    }
  }

  async start(issue: TaskListItem): Promise<void> {
    this.error.set(null);
    try {
      await this.timer.start(issue.issueId, issue.summary);
    } catch (err) {
      this.error.set((err as Error).message);
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

  async ai(action: AiAction): Promise<void> {
    const devParam = this.dev.devParam();
    const monday = startOfWeek(new Date());
    const from = toIsoDate(monday);
    const to = toIsoDate(addDays(monday, 6));
    this.aiBusy.set(action);
    this.error.set(null);
    try {
      switch (action) {
        case 'draft':
          this.draftResult.set(await this.api.aiDraft(this.freeText().trim(), this.today, devParam));
          break;
        case 'gaps':
          this.draftResult.set(await this.api.aiGapfills(from, to, devParam));
          break;
        case 'summary':
          this.summaryText.set((await this.api.aiSummary(this.today, this.today, devParam)).text);
          break;
        case 'triage':
          this.triageResult.set(await this.api.aiTriage(devParam));
          break;
      }
    } catch (err) {
      this.error.set((err as Error).message);
    } finally {
      this.aiBusy.set(null);
    }
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
}
