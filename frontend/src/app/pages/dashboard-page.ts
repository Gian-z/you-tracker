import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { InlineBook } from '../components/inline-book';
import { DraftReviewDialog } from '../dialogs/draft-review-dialog';
import { LogTimeDialog } from '../dialogs/log-time-dialog';
import { SubtaskChoice, SubtaskPickerDialog } from '../dialogs/subtask-picker-dialog';
import {
  addDays,
  formatClock,
  formatDayLabel,
  formatDuration,
  formatScore,
  startOfWeek,
  toIsoDate,
} from '../format';
import {
  BookingPreset,
  BookingTarget,
  DaySummary,
  DraftResult,
  TaskListItem,
  TimeOverview,
  TriageResult,
  WorkLogRequest,
} from '../models';
import { ApiService } from '../services/api.service';
import { BookingService } from '../services/booking.service';
import { DevService } from '../services/dev.service';
import { RefreshService } from '../services/refresh.service';
import { SearchService } from '../services/search.service';
import { TimerService } from '../services/timer.service';

type AiAction = 'draft' | 'gaps' | 'summary-day' | 'summary-week' | 'triage';

const TOP_TICKET_COUNT = 5;

/**
 * "Heute" cockpit: answers "habe ich heute/diese Woche alles gebucht?" at a glance
 * and is the fastest booking surface (presets, inline booking, search, assistant).
 */
@Component({
  selector: 'app-dashboard-page',
  imports: [
    FormsModule,
    RouterLink,
    DraftReviewDialog,
    LogTimeDialog,
    SubtaskPickerDialog,
    InlineBook,
  ],
  template: `
    <div class="page dashboard">
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

      <!-- Row 1: today hero meter · week strip · fokus -->
      <div class="tiles hero-tiles">
        <div class="tile hero-tile">
          <div class="tile-caption">Heute</div>
          <div class="tile-value">
            {{ clock(todayBooked()) }} <span class="muted hero-target">/ {{ clock(targetToday()) }}</span>
          </div>
          <div class="meter hero-meter">
            <span
              class="meter-fill"
              [class.ok]="todayGap() === 0 && targetToday() > 0"
              [style.width.%]="todayPercent()"
            ></span>
          </div>
          <div class="tile-sub muted">
            @if (todayGap() > 0) {
              <span class="badge amber">Lücke {{ dur(todayGap()) }}</span>
            } @else if (targetToday() > 0) {
              <span class="badge green">Tagesziel erreicht</span>
            }
          </div>
        </div>
        <div class="tile">
          <div class="tile-caption">Woche</div>
          <div class="tile-value">{{ clock(week()?.totalBookedMinutes ?? 0) }}</div>
          <div class="week-strip" title="Zur Wochenansicht">
            @for (d of weekdays(); track d.date) {
              <a
                routerLink="/woche"
                class="week-cell"
                [class]="'week-cell heat-' + dayState(d)"
                [title]="dayLabel(d.date) + ': ' + clock(d.bookedMinutes) + ' / ' + clock(d.targetMinutes)"
              >
                {{ dayLetter(d.date) }}
              </a>
            }
          </div>
          <div class="tile-sub muted">von {{ clock(week()?.totalTargetMinutes ?? 0) }}</div>
        </div>
        <div class="tile">
          <div class="tile-caption">Fokus-Score</div>
          <div class="tile-value">{{ score(week()?.averageFokusScore ?? null) }}</div>
          <div class="tile-sub muted">Wochenschnitt</div>
        </div>
      </div>

      <!-- Row 2: quick booking -->
      @if (dev.isSelf()) {
        <section class="card">
          <h2>Schnellbuchung</h2>
          <div class="presets-strip">
            @for (p of presets(); track p.id) {
              <span class="preset-chip" [class.busy]="bookingPresetId() === p.id">
                <button
                  type="button"
                  class="preset-book"
                  [disabled]="bookingPresetId() !== null"
                  (click)="bookPreset(p)"
                  [title]="p.issueId + ' · bucht ' + dur(p.minutes) + ' für heute'"
                >
                  {{ p.name }} · {{ dur(p.minutes) }}
                </button>
                <button
                  type="button"
                  class="preset-delete"
                  title="Preset löschen"
                  [disabled]="bookingPresetId() !== null"
                  (click)="deletePreset(p)"
                >
                  ×
                </button>
              </span>
            } @empty {
              <span class="muted small">
                Keine Presets — im Buchen-Dialog «Als Preset speichern» anhaken.
              </span>
            }
            <span class="flex-spacer"></span>
            <button type="button" (click)="search.open.set(true)" title="Ticket ausserhalb des eigenen Scopes suchen">
              🔍 Anderes Ticket… <span class="muted small">Ctrl+K</span>
            </button>
          </div>
        </section>
      }

      <!-- Row 3: today's bookings | top tickets -->
      <div class="dash-grid">
        <section class="card">
          <h2>Heutige Buchungen</h2>
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
              <tfoot>
                <tr>
                  <td></td>
                  <td>Total</td>
                  <td class="num nowrap">{{ dur(todayBooked()) }}</td>
                </tr>
              </tfoot>
            </table>
          } @else {
            <div class="muted small">Heute noch nichts gebucht.</div>
          }
        </section>

        <section class="card">
          <h2>
            Top-Tickets
            <span class="flex-spacer"></span>
            <a routerLink="/tickets" class="small">Alle Tickets →</a>
          </h2>
          @if (loading()) {
            <div class="loading"><span class="spinner"></span> Laden…</div>
          } @else {
            <table class="data-table compact">
              <tbody>
                @for (t of topTickets(); track t.issueId) {
                  <tr>
                    <td class="nowrap">
                      <a [href]="t.webUrl" target="_blank" rel="noopener">{{ t.issueId }}</a>
                      @if (booking.isFeature(t)) {
                        <span class="redirect-badge" [title]="redirectHint(t)">↪</span>
                      }
                    </td>
                    <td class="nowrap">{{ t.state ?? '–' }}</td>
                    <td class="summary-cell">{{ t.summary }}</td>
                    @if (dev.isSelf()) {
                      <td class="nowrap inline-book-cell">
                        <app-inline-book [issue]="t" />
                      </td>
                    }
                    <td class="nowrap row-actions always-visible">
                      @if (dev.isSelf()) {
                        <button type="button" class="icon" title="Timer starten" (click)="start(t)">▶</button>
                        <button type="button" class="icon" title="Zeit buchen" (click)="logIssue.set(t)">✎</button>
                      }
                    </td>
                  </tr>
                } @empty {
                  <tr><td class="muted empty-cell">Keine Tickets.</td></tr>
                }
              </tbody>
            </table>
          }
        </section>
      </div>

      <!-- Row 4: assistant -->
      <section class="card">
        <h2>Assistent</h2>
        <textarea
          rows="2"
          [(ngModel)]="freeText"
          placeholder="Beschreibe deinen Tag… ('vormittags XBOX-587 Testing, nachmittags Reviews')"
          [disabled]="aiBusy() !== null"
        ></textarea>
        <div class="toolbar wrap">
          <label class="inline-label">
            Datum
            <input type="date" [(ngModel)]="aiDate" [disabled]="aiBusy() !== null" />
          </label>
          <button type="button" class="primary" (click)="ai('draft')" [disabled]="aiBusy() !== null || !freeText().trim()">
            Arbeitslog entwerfen
          </button>
          <button type="button" (click)="ai('gaps')" [disabled]="aiBusy() !== null">Wochenlücken füllen</button>
          <button type="button" (click)="ai('summary-day')" [disabled]="aiBusy() !== null">Tag zusammenfassen</button>
          <button type="button" (click)="ai('summary-week')" [disabled]="aiBusy() !== null">Woche zusammenfassen</button>
          <button type="button" (click)="ai('triage')" [disabled]="aiBusy() !== null">Triage</button>
        </div>
        @if (aiBusy()) {
          <div class="banner info"><span class="spinner"></span> Claude denkt nach… das kann eine Minute dauern</div>
        }
        @if (summaryText(); as text) {
          <div class="summary-output">{{ text }}</div>
        }
        @if (triageResult(); as t) {
          @if (t.focusSuggestion) {
            <div class="focus-suggestion"><strong>Fokus:</strong> {{ t.focusSuggestion }}</div>
          }
          <ol class="triage-list">
            @for (entry of t.ranked; track entry.issueId) {
              <li>
                <div class="triage-head">
                  <span class="rank">#{{ entry.rank }}</span>
                  @if (issueUrl(entry.issueId); as url) {
                    <a [href]="url" target="_blank" rel="noopener" class="issue-id">{{ entry.issueId }}</a>
                  } @else {
                    <span class="issue-id">{{ entry.issueId }}</span>
                  }
                  <span>{{ entry.summary }}</span>
                  <span class="muted small">Score {{ entry.score }}</span>
                </div>
                @if (entry.reasons.length > 0) {
                  <ul class="reasons small muted">
                    @for (reason of entry.reasons; track $index) {
                      <li>{{ reason }}</li>
                    }
                  </ul>
                }
              </li>
            }
          </ol>
          @if (t.sprintSuggestions.length > 0) {
            <h3 class="sprint-suggestions-title">Vorschläge aus dem Sprint-Pool (unbeansprucht)</h3>
            <ol class="triage-list sprint-suggestions">
              @for (entry of t.sprintSuggestions; track entry.issueId) {
                <li>
                  <div class="triage-head">
                    <span class="badge green">übernehmen?</span>
                    @if (issueUrl(entry.issueId); as url) {
                      <a [href]="url" target="_blank" rel="noopener" class="issue-id">{{ entry.issueId }}</a>
                    } @else {
                      <span class="issue-id">{{ entry.issueId }}</span>
                    }
                    <span>{{ entry.summary }}</span>
                    <span class="muted small">Match {{ entry.score }}</span>
                  </div>
                  @if (entry.reasons.length > 0) {
                    <ul class="reasons small muted">
                      @for (reason of entry.reasons; track $index) {
                        <li>{{ reason }}</li>
                      }
                    </ul>
                  }
                </li>
              }
            </ol>
          }
        }
      </section>
    </div>

    @if (logIssue(); as issue) {
      <app-log-time-dialog [issueId]="issue.issueId" [issueSummary]="issue.summary" (closed)="logIssue.set(null)" />
    }
    @if (draftResult(); as result) {
      <app-draft-review-dialog [drafts]="result.drafts" [unmatched]="result.unmatched" (closed)="draftResult.set(null)" />
    }
    @if (pickerTarget(); as target) {
      <app-subtask-picker-dialog
        [target]="target"
        (chosen)="onPickerChosen($event)"
        (closed)="pickerTarget.set(null)"
      />
    }
  `,
})
export class DashboardPage {
  private readonly api = inject(ApiService);
  private readonly refresh = inject(RefreshService);
  private readonly timer = inject(TimerService);
  protected readonly dev = inject(DevService);
  protected readonly booking = inject(BookingService);
  protected readonly search = inject(SearchService);

  readonly issues = signal<TaskListItem[]>([]);
  readonly pool = signal<TaskListItem[]>([]);
  readonly week = signal<TimeOverview | null>(null);
  readonly presets = signal<BookingPreset[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly logIssue = signal<TaskListItem | null>(null);
  readonly bookingPresetId = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly pickerTarget = signal<BookingTarget | null>(null);
  private pendingRequest: WorkLogRequest | null = null;
  readonly aiBusy = signal<AiAction | null>(null);
  readonly freeText = signal('');
  readonly aiDate = signal(toIsoDate(new Date()));
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
  readonly todayPercent = computed(() => {
    const target = this.targetToday();
    return target > 0 ? Math.min(100, (this.todayBooked() / target) * 100) : 0;
  });

  /** Mo–Fr cells for the week strip. */
  readonly weekdays = computed(() => (this.week()?.days ?? []).filter((d) => d.isWorkday));

  readonly topTickets = computed(() => this.issues().slice(0, TOP_TICKET_COUNT));

  /** Issue-id → web url for triage result links (own tickets + sprint pool). */
  private readonly webUrls = computed(
    () => new Map([...this.issues(), ...this.pool()].map((i) => [i.issueId, i.webUrl])),
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
    // Spinner only on first load — background reloads (after a booking) must not
    // tear down the table and its inline-book success chips.
    if (this.issues().length === 0) {
      this.loading.set(true);
    }
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
      this.booking.prefetch(issues.slice(0, TOP_TICKET_COUNT)); // ↪ badge tooltips
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
    const request: WorkLogRequest = {
      issueId: preset.issueId,
      date: this.today,
      minutes: preset.minutes,
      typeId: preset.typeId,
      text: preset.comment,
    };
    try {
      const outcome = await this.booking.bookWithPolicy(request);
      if (outcome.status === 'needs-picker') {
        this.pendingRequest = request;
        this.pickerTarget.set(outcome.target);
      } else {
        this.showBooked(outcome.item.issueId, request.minutes, outcome.redirectedFrom);
      }
    } catch (err) {
      this.error.set((err as Error).message);
    } finally {
      this.bookingPresetId.set(null);
    }
  }

  async deletePreset(preset: BookingPreset): Promise<void> {
    try {
      await this.api.deletePreset(preset.id);
      this.presets.set(await this.api.getPresets());
    } catch (err) {
      this.error.set((err as Error).message);
    }
  }

  async onPickerChosen(choice: SubtaskChoice): Promise<void> {
    const request = this.pendingRequest;
    this.pickerTarget.set(null);
    this.pendingRequest = null;
    if (!request) {
      return;
    }
    try {
      const item = await this.booking.book({
        ...request,
        issueId: choice.issueId,
        allowFeature: choice.allowFeature,
      });
      this.showBooked(item.issueId, request.minutes, null);
    } catch (err) {
      this.error.set((err as Error).message);
    }
  }

  private showBooked(issueId: string, minutes: number, redirectedFrom: string | null): void {
    const suffix = redirectedFrom ? ` (umgeleitet von ${redirectedFrom})` : '';
    this.notice.set(`${formatDuration(minutes)} auf ${issueId} gebucht${suffix}`);
    setTimeout(() => this.notice.set(null), 5000);
  }

  redirectHint(item: TaskListItem): string {
    const target = this.booking.resolutionFor(item.issueId);
    if (!target) {
      return 'Feature – Buchungen landen auf der Task-Teilaufgabe';
    }
    switch (target.kind) {
      case 'redirected':
        return `Buchungen landen auf ${target.targetIssueId} – ${target.targetSummary}`;
      case 'ambiguous':
        return `${target.candidates.length} Task-Teilaufgaben – beim Buchen wählen`;
      case 'noTask':
        return 'Feature ohne Task-Teilaufgabe!';
      default:
        return '';
    }
  }

  issueUrl(issueId: string): string | null {
    return this.webUrls().get(issueId) ?? null;
  }

  dayState(day: DaySummary): string {
    if (day.date === this.today) {
      return 'today';
    }
    if (day.date > this.today) {
      return 'future';
    }
    if (day.bookedMinutes >= day.targetMinutes && day.targetMinutes > 0) {
      return 'reached';
    }
    return day.bookedMinutes > 0 ? 'partial' : 'none';
  }

  dayLetter(date: string): string {
    return formatDayLabel(date).slice(0, 2);
  }

  dayLabel(date: string): string {
    return formatDayLabel(date);
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
          this.draftResult.set(
            await this.api.aiDraft(this.freeText().trim(), this.aiDate(), devParam),
          );
          break;
        case 'gaps':
          this.draftResult.set(await this.api.aiGapfills(from, to, devParam));
          break;
        case 'summary-day':
          this.summaryText.set(
            (await this.api.aiSummary(this.aiDate(), this.aiDate(), devParam)).text,
          );
          break;
        case 'summary-week':
          this.summaryText.set((await this.api.aiSummary(from, to, devParam)).text);
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
