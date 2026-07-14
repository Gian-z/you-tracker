import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { InlineBook } from '../components/inline-book';
import { DraftReviewDialog } from '../dialogs/draft-review-dialog';
import { EditWorkItemDialog } from '../dialogs/edit-work-item-dialog';
import { LogTimeDialog } from '../dialogs/log-time-dialog';
import { SubtaskChoice, SubtaskPickerDialog } from '../dialogs/subtask-picker-dialog';
import {
  addDays,
  formatClock,
  formatDayLabel,
  formatDuration,
  formatScore,
  formatWallClock,
  parseClock,
  parseDuration,
  startOfWeek,
  toIsoDate,
} from '../format';
import {
  BookingPreset,
  BookingTarget,
  DraftResult,
  TaskListItem,
  TeamConfig,
  TeamSprint,
  TimeOverview,
  TriageResult,
  WorkItem,
  WorkLogRequest,
} from '../models';
import { ApiService } from '../services/api.service';
import { BookingService } from '../services/booking.service';
import { DayStateService } from '../services/day-state.service';
import { DayTargetService } from '../services/day-target.service';
import { DevService } from '../services/dev.service';
import { RefreshService } from '../services/refresh.service';
import { SettingsService } from '../services/settings.service';
import { SettingsUiService } from '../services/settings-ui.service';
import { TimerService } from '../services/timer.service';
import { ToastService } from '../services/toast.service';
import { TodayStatusService } from '../services/today-status.service';

type AiAction = 'draft' | 'gaps' | 'summary-week' | 'triage' | 'meetings';

const TOP_TICKET_COUNT = 5;

/**
 * "Heute" cockpit: answers "habe ich heute/diese Woche alles gebucht?" at a glance
 * and is the fastest booking surface (presence stamps, presets, inline booking, assistant).
 */
@Component({
  selector: 'app-dashboard-page',
  imports: [
    FormsModule,
    RouterLink,
    DraftReviewDialog,
    EditWorkItemDialog,
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

      <!-- header -->
      <div style="display:flex;align-items:baseline;gap:0.75rem;flex-wrap:wrap">
        <h1 style="margin:0">Heute</h1>
        <span class="muted small">
          {{ headerDate }}
          @if (sprintHeader(); as sh) {
            · {{ sh }}
          }
        </span>
      </div>

      <!-- hero tiles -->
      <div class="tiles hero-tiles">
        <!-- tile: heute gebucht -->
        <div class="tile">
          <div class="tile-caption">Heute gebucht</div>
          <div class="tile-value num">
            {{ clock(todayBooked()) }}
            <span class="muted hero-target">von {{ clock(targetToday()) }} {{ dayTarget.targetSource() }}</span>
          </div>
          <div class="meter hero-meter">
            <span
              class="meter-fill"
              [class.ok]="todayReached()"
              [class.warn]="!todayReached()"
              [style.width.%]="todayPercent()"
            ></span>
          </div>
          @if (todayReached()) {
            <span class="gap-pill ok">Tagesziel erreicht ✓</span>
          } @else {
            <button type="button" class="gap-pill warn" title="Klick bucht den Rest des Tages" (click)="openGapBooking()">
              Lücke {{ clock(todayGap()) }} — jetzt buchen
            </button>
          }
        </div>

        <!-- tile: woche -->
        <div class="tile">
          <div class="tile-caption">Diese Woche</div>
          <div class="tile-value num">
            {{ clock(weekBooked()) }}
            <span class="muted hero-target">von {{ clock(weekTarget()) }}</span>
          </div>
          <div class="week-strip" title="Zur Wochenansicht">
            @for (c of weekCells(); track c.date) {
              <a routerLink="/woche" [class]="'week-cell heat-' + c.heat" [title]="c.tip">{{ dayLetter(c.date) }}</a>
            }
          </div>
          <div class="tile-sub muted">
            Wochenlücke:
            <span class="num" style="font-weight:600" [style.color]="weekGap() > 0 ? 'var(--warn)' : null">
              {{ clock(weekGap()) }}
            </span>
          </div>
        </div>

        <!-- tile: fokus -->
        <div class="tile">
          <div style="display:flex;justify-content:space-between;align-items:center;gap:0.5rem">
            <div class="tile-caption" style="margin:0">Fokus-Score</div>
            @if (todayFokus()) {
              <button type="button" class="link small" (click)="fokusDetails.set(!fokusDetails())">
                {{ fokusDetails() ? 'Schliessen' : 'Details' }}
              </button>
            }
          </div>
          <div class="tile-value num" [style.color]="fokusColor()">
            {{ score(todayFokus()?.score ?? null) }}
            <span class="muted hero-target">/ 100</span>
          </div>
          @if (todayFokus(); as f) {
            @if (fokusDetails()) {
              <div class="tile-sub" style="display:flex;flex-direction:column;gap:0.25rem">
                <div style="display:flex;justify-content:space-between;gap:0.5rem">
                  <span>{{ f.switches }} Tickets heute</span>
                  @if (f.switchPenalty > 0) {
                    <span class="num fokus-penalty">−{{ f.switchPenalty }}</span>
                  }
                </div>
                <div style="display:flex;justify-content:space-between;gap:0.5rem">
                  <span>Deep Work {{ f.deepPercent }}%</span>
                  @if (f.deepPenalty > 0) {
                    <span class="num fokus-penalty">−{{ f.deepPenalty }}</span>
                  }
                </div>
                @for (frag of fragmentedBookings(); track frag.issueId) {
                  <div style="display:flex;justify-content:space-between;gap:0.5rem" class="muted">
                    <span><span class="issue-id">{{ frag.issueId }}</span> {{ frag.count }}× kurz</span>
                    <span class="num">{{ dur(frag.minutes) }}</span>
                  </div>
                }
              </div>
            } @else {
              <div class="tile-sub muted">{{ fokusVerdict() }}</div>
            }
          } @else {
            <div class="tile-sub muted">{{ fokusVerdict() }}</div>
          }
        </div>

        <!-- tile: sprint -->
        @if (currentSprint(); as s) {
          <div class="tile">
            <div class="tile-caption">Sprint {{ s.name }}</div>
            @if (sprintDays(); as d) {
              <div class="tile-value num">
                Tag {{ d.done }}<span class="muted hero-target">/ {{ d.total }}</span>
              </div>
            }
            @if (effortTotals(); as e) {
              <div class="tile-sub" [class.over-effort]="e.spent > e.estimate">
                Ist <span class="num" style="font-weight:600">{{ clock(e.spent) }}</span>
                <span class="muted">· Plan <span class="num">{{ clock(e.estimate) }}</span></span>
              </div>
            }
            @if (ticketStates().length > 0) {
              <div class="state-chips sprint-states">
                @for (st of ticketStates(); track st.state) {
                  <a routerLink="/tickets" class="state-chip">{{ st.count }} {{ st.state }}</a>
                }
              </div>
            }
          </div>
        }
      </div>

      <!-- overbooked warning -->
      @if (todayStatus.overbookedMinutes() > 0) {
        <div
          class="banner"
          style="color:var(--warn);background:var(--warn-bg);border-color:color-mix(in srgb, var(--warn) 35%, transparent)"
        >
          ⚠ Du hast {{ clock(todayStatus.bookedMinutes()) }} gebucht, aber dein Soll ist nur
          {{ clock(todayStatus.targetMinutes()) }} — Buchungen prüfen.
        </div>
      }

      <!-- präsenz -->
      @if (dev.isSelf()) {
        <section class="card presence-card">
          <span class="caption">Präsenz</span>
          <span class="presence-label">Komme</span>
          <input
            class="presence-input"
            [value]="dayState.today().come ?? ''"
            (change)="onComeChange($any($event.target).value)"
            placeholder="7:45"
            aria-label="Komme"
          />
          <button type="button" class="presence-now" title="Auf jetzt stempeln" (click)="stampCome()">Jetzt</button>
          <span class="presence-label" style="margin-left:0.5rem">Gehe</span>
          <input
            class="presence-input"
            [value]="dayState.today().go ?? ''"
            (change)="onGoChange($any($event.target).value)"
            placeholder="läuft…"
            aria-label="Gehe"
          />
          <button type="button" class="presence-now" title="Auf jetzt stempeln — pausiert den Timer" (click)="stampGo()">
            Jetzt
          </button>
          <span class="presence-label" style="margin-left:0.5rem">Pause</span>
          <input
            class="presence-input narrow"
            [value]="pauseDisplay()"
            (change)="onPauseChange($any($event.target).value)"
            placeholder="30m"
            aria-label="Pause"
          />
          <button type="button" class="presence-chip" (click)="setPause(30)">30m</button>
          <button type="button" class="presence-chip" (click)="setPause(60)">1h</button>
          <span class="presence-summary">
            @if (dayTarget.presenceMinutes() !== null) {
              Präsenz (Ist) <span class="num">{{ clock(dayTarget.presenceMinutes()!) }}</span>
              @if (dayTarget.presenceRunning()) {
                <span class="muted" style="margin-left:0.25rem">(läuft)</span>
              }
              · Soll <span class="num">{{ clock(dayTarget.sollToday()) }}</span> · Saldo
              <span
                class="num"
                [class.saldo-pos]="(dayTarget.saldoMinutes() ?? 0) >= 0"
                [class.saldo-neg]="(dayTarget.saldoMinutes() ?? 0) < 0"
              >{{ saldoLabel() }}</span>
            } @else {
              Soll <span class="num">{{ clock(dayTarget.sollToday()) }}</span>
            }
          </span>
        </section>
      }

      <!-- schnellbuchung -->
      @if (dev.isSelf()) {
        <div class="quick-strip">
          <span class="caption">Schnellbuchung</span>
          @for (p of presets(); track p.id) {
            <span class="preset-chip" [class.busy]="bookingPresetId() === p.id">
              <button
                type="button"
                class="preset-book"
                [disabled]="bookingPresetId() !== null"
                (click)="bookPreset(p)"
                [title]="p.issueId + ' · bucht ' + dur(settings.round(p.minutes)) + ' für heute'"
              >
                {{ p.name }} <span class="num" style="color:var(--accent-strong)">{{ dur(p.minutes) }}</span>
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
            @if (!restChipVisible()) {
              <span class="muted small">Keine Presets — im Buchen-Dialog «Als Preset speichern» anhaken.</span>
            }
          }
          @if (restChipVisible()) {
            <span class="preset-chip" [class.busy]="bookingPresetId() === 'rest'">
              <button
                type="button"
                class="preset-book"
                [disabled]="bookingPresetId() !== null"
                (click)="bookRest()"
                [title]="'Bucht die Lücke exakt auf ' + settings.defaultIssueId()"
              >
                Rest des Tages <span class="num" style="color:var(--accent-strong)">{{ clock(todayGap()) }}</span>
              </button>
            </span>
          }
          <span class="flex-spacer"></span>
          <button type="button" class="link small" (click)="settingsUi.show('presets')">Bearbeiten…</button>
        </div>
      }

      <!-- assistent -->
      @if (settings.aiOn()) {
        <section class="assistant-card">
          <div class="assistant-head">
            <span class="assistant-icon">✦</span>
            <span style="font-weight:600">Assistent</span>
            <span class="assistant-note">schlägt nur vor — du bestätigst</span>
            <span class="flex-spacer"></span>
            <button type="button" (click)="describeOpen.set(!describeOpen())" [disabled]="aiBusy() !== null">
              Tag beschreiben → Entwürfe
            </button>
            <button type="button" (click)="ai('gaps')" [disabled]="aiBusy() !== null">
              Lücken füllen <span class="muted">(Kalender + Git)</span>
            </button>
            @if (calendarEnabled() && dev.isSelf()) {
              <button
                type="button"
                (click)="ai('meetings')"
                [disabled]="aiBusy() !== null"
                title="Kalender-Termine des Tages per Regel auf Tickets mappen (bestätigen vor Buchen)"
              >
                Meetings buchen
              </button>
            }
            <button type="button" (click)="ai('summary-week')" [disabled]="aiBusy() !== null">Woche zusammenfassen</button>
            <button type="button" (click)="ai('triage')" [disabled]="aiBusy() !== null">Triage</button>
          </div>
          @if (describeOpen()) {
            <div style="display:flex;gap:0.5rem;margin-top:0.7rem;align-items:flex-start;flex-wrap:wrap">
              <textarea
                rows="2"
                style="width:auto;flex:1;min-width:16rem"
                [(ngModel)]="freeText"
                placeholder="Beschreibe deinen Tag… ('vormittags XBOX-587 Testing, nachmittags Reviews')"
                [disabled]="aiBusy() !== null"
              ></textarea>
              <input type="date" [(ngModel)]="aiDate" [disabled]="aiBusy() !== null" aria-label="Datum" />
              <button
                type="button"
                class="primary"
                (click)="ai('draft')"
                [disabled]="aiBusy() !== null || !freeText().trim()"
              >
                Entwerfen
              </button>
            </div>
          }
          @if (aiBusy()) {
            <div class="assistant-busy"><span class="pulse">✦</span> Claude denkt nach… das kann eine Minute dauern.</div>
          }
          @if (summaryText(); as text) {
            <div class="summary-text" style="margin-top:0.7rem">{{ text }}</div>
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
      }

      <!-- bottom grid: heutige buchungen | top-tickets -->
      <div class="dash-grid">
        <section class="card flush">
          <div class="card-head">
            <span>Heutige Buchungen</span>
            @if (dev.isSelf()) {
              <button type="button" class="primary" (click)="openNewBooking()">+ Buchen</button>
            }
          </div>
          @for (item of todayItems(); track item.id) {
            <div style="display:flex;align-items:center;gap:0.6rem;padding:0.5rem 1rem;border-bottom:1px solid var(--border)">
              @if (issueUrl(item.issueId); as url) {
                <a [href]="url" target="_blank" rel="noopener" class="issue-id">{{ item.issueId }}</a>
              } @else {
                <span class="issue-id">{{ item.issueId }}</span>
              }
              <span class="small" style="flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">
                {{ item.text || item.issueSummary }}
              </span>
              <span class="num" style="font-weight:600">{{ dur(item.minutes) }}</span>
              @if (dev.isSelf()) {
                <span class="row-actions always-visible">
                  <button
                    type="button"
                    class="icon"
                    title="Buchung bearbeiten"
                    [disabled]="deletingId() !== null"
                    (click)="editItem.set(item)"
                  >
                    ✎
                  </button>
                  @if (confirmDeleteId() === item.id) {
                    <button
                      type="button"
                      class="icon danger"
                      title="Wirklich löschen?"
                      [disabled]="deletingId() !== null"
                      (click)="deleteItem(item)"
                    >
                      Löschen?
                    </button>
                  } @else {
                    <button
                      type="button"
                      class="icon"
                      title="Buchung löschen"
                      [disabled]="deletingId() !== null"
                      (click)="armDelete(item)"
                    >
                      ✕
                    </button>
                  }
                </span>
              }
            </div>
          } @empty {
            <div class="muted small" style="padding:0.75rem 1rem">Heute noch nichts gebucht.</div>
          }
          <div class="card-foot">
            <span>{{ todayItems().length }} {{ todayItems().length === 1 ? 'Buchung' : 'Buchungen' }}</span>
            <span class="num" style="font-weight:600">{{ clock(todayBooked()) }}</span>
          </div>
        </section>

        <section class="card flush">
          <div class="card-head">
            <span>Deine Top-Tickets</span>
            <a routerLink="/tickets" class="small">Alle Tickets →</a>
          </div>
          @if (loading()) {
            <div class="loading" style="padding:0.75rem 1rem"><span class="spinner"></span> Laden…</div>
          } @else {
            @for (t of topTickets(); track t.issueId) {
              <div style="display:flex;align-items:center;gap:0.6rem;padding:0.5rem 1rem;border-bottom:1px solid var(--border)">
                <span class="state-chip">{{ t.state ?? '–' }}</span>
                <a [href]="t.webUrl" target="_blank" rel="noopener" class="issue-id">{{ t.issueId }}</a>
                <span class="small" style="flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">
                  {{ t.summary }}
                  @if (booking.isFeature(t)) {
                    <span class="redirect-badge" [title]="redirectHint(t)">↪</span>
                  }
                </span>
                @if (effortFor(t); as e) {
                  <span class="num small" [style.color]="e.over ? 'var(--warn)' : null">{{ e.label }}</span>
                }
                @if (dev.isSelf()) {
                  <span class="row-actions always-visible">
                    <button type="button" class="icon" title="Buchung erfassen (Dialog)" (click)="openLog(t)">✎</button>
                    <button type="button" class="icon" title="Timer auf diesem Ticket starten" (click)="start(t)">▶</button>
                  </span>
                  <app-inline-book [issue]="t" />
                }
              </div>
            } @empty {
              <div class="muted small" style="padding:0.75rem 1rem">Keine Tickets.</div>
            }
            <div class="hint-foot">
              ↵ Enter bucht auf heute · Format <span class="num">45m</span>, <span class="num">1h30</span> oder
              <span class="num">45m: Daily</span>
            </div>
          }
        </section>
      </div>
    </div>

    @if (logDialog(); as d) {
      <app-log-time-dialog
        [issueId]="d.issueId"
        [issueSummary]="d.issueSummary"
        [initialMinutes]="d.minutes"
        (closed)="logDialog.set(null)"
      />
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
    @if (editItem(); as item) {
      <app-edit-work-item-dialog [item]="item" (closed)="editItem.set(null)" />
    }
  `,
})
export class DashboardPage {
  private readonly api = inject(ApiService);
  private readonly refresh = inject(RefreshService);
  private readonly timer = inject(TimerService);
  private readonly toast = inject(ToastService);
  protected readonly dev = inject(DevService);
  protected readonly booking = inject(BookingService);
  protected readonly settings = inject(SettingsService);
  protected readonly settingsUi = inject(SettingsUiService);
  protected readonly dayState = inject(DayStateService);
  protected readonly dayTarget = inject(DayTargetService);
  protected readonly todayStatus = inject(TodayStatusService);

  readonly issues = signal<TaskListItem[]>([]);
  readonly pool = signal<TaskListItem[]>([]);
  readonly week = signal<TimeOverview | null>(null);
  readonly presets = signal<BookingPreset[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly logDialog = signal<{ issueId: string; issueSummary: string; minutes: number | null } | null>(null);
  readonly bookingPresetId = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly pickerTarget = signal<BookingTarget | null>(null);
  private pendingRequest: WorkLogRequest | null = null;
  readonly editItem = signal<WorkItem | null>(null);
  readonly confirmDeleteId = signal<string | null>(null);
  readonly deletingId = signal<string | null>(null);
  private deleteResetHandle: ReturnType<typeof setTimeout> | null = null;
  readonly aiBusy = signal<AiAction | null>(null);
  readonly describeOpen = signal(false);
  readonly freeText = signal('');
  readonly aiDate = signal(toIsoDate(new Date()));
  readonly calendarEnabled = signal(false);
  readonly draftResult = signal<DraftResult | null>(null);
  readonly summaryText = signal<string | null>(null);
  readonly triageResult = signal<TriageResult | null>(null);
  readonly fokusDetails = signal(false);
  readonly team = signal<TeamConfig | null>(null);

  readonly today = toIsoDate(new Date());

  /** "Donnerstag, 16. Juli 2026" for the header subline. */
  readonly headerDate = new Date().toLocaleDateString('de-DE', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    year: 'numeric',
  });

  readonly todayItems = computed(
    () => this.week()?.days.find((d) => d.date === this.today)?.items ?? [],
  );
  readonly todayBooked = computed(() =>
    this.todayItems().reduce((sum, item) => sum + item.minutes, 0),
  );
  /** Presence-aware daily target (Präsenz or Soll — DayTargetService semantics). */
  readonly targetToday = computed(() => this.dayTarget.targetToday());
  readonly todayGap = computed(() => Math.max(0, this.targetToday() - this.todayBooked()));
  readonly todayReached = computed(() => this.targetToday() > 0 && this.todayGap() === 0);
  readonly todayPercent = computed(() => {
    const target = this.targetToday();
    return target > 0 ? Math.min(100, (this.todayBooked() / target) * 100) : 0;
  });

  /** Mo–Fr cells for the week strip, measured against the presence/absence-aware targets. */
  readonly weekCells = computed(() => {
    const days = (this.week()?.days ?? []).filter((d) => {
      const dow = new Date(`${d.date}T00:00:00`).getDay();
      return dow >= 1 && dow <= 5;
    });
    return days.map((d) => {
      const isToday = d.date === this.today;
      const target = this.dayTarget.targetFor(d.date, isToday);
      return {
        date: d.date,
        booked: d.bookedMinutes,
        target,
        heat: this.heatFor(d.date, d.bookedMinutes, target, isToday),
        tip:
          formatDayLabel(d.date) +
          (target === 0 ? ': Absenz' : `: ${formatClock(d.bookedMinutes)} / ${formatClock(target)}`),
      };
    });
  });
  readonly weekBooked = computed(() => this.weekCells().reduce((sum, c) => sum + c.booked, 0));
  readonly weekTarget = computed(() => this.weekCells().reduce((sum, c) => sum + c.target, 0));
  readonly weekGap = computed(() => Math.max(0, this.weekTarget() - this.weekBooked()));

  /**
   * Today's Fokus breakdown — same formula as MetricsCalculator.FokusScore:
   * 100 − 12×max(0, switches−2) − 30×(1−deepShare). Keep the constants in sync.
   */
  readonly todayFokus = computed(() => {
    const items = this.todayItems();
    const booked = this.todayBooked();
    if (booked === 0) {
      return null;
    }
    const switches = new Set(items.map((i) => i.issueId)).size;
    const deepShare =
      items.filter((i) => i.minutes >= 60).reduce((sum, i) => sum + i.minutes, 0) / booked;
    const switchPenalty = 12 * Math.max(0, switches - 2);
    const deepPenalty = Math.round(30 * (1 - deepShare));
    return {
      score: Math.min(100, Math.max(0, Math.round(100 - switchPenalty - 30 * (1 - deepShare)))),
      switches,
      switchPenalty,
      deepPercent: Math.round(deepShare * 100),
      deepPenalty,
    };
  });

  readonly fokusColor = computed(() => {
    const s = this.todayFokus()?.score ?? null;
    if (s === null) {
      return null;
    }
    return s >= 75 ? 'var(--ok)' : s >= 60 ? 'var(--warn)' : 'var(--danger)';
  });

  /** One-line verdict for the collapsed fokus tile. */
  readonly fokusVerdict = computed(() => {
    const f = this.todayFokus();
    if (!f) {
      return 'Heute noch nichts gebucht.';
    }
    if (f.score >= 75) {
      return 'Stark — lange Blöcke, wenig Wechsel.';
    }
    if (f.score >= 60) {
      return 'Solide — wenig Fragmentierung heute.';
    }
    return 'Fragmentiert — viele kurze Buchungen.';
  });

  /** Today's sub-hour bookings grouped by ticket — what fragments the day. */
  readonly fragmentedBookings = computed(() => {
    const groups = new Map<string, { count: number; minutes: number }>();
    for (const item of this.todayItems().filter((i) => i.minutes < 60)) {
      const entry = groups.get(item.issueId) ?? { count: 0, minutes: 0 };
      entry.count += 1;
      entry.minutes += item.minutes;
      groups.set(item.issueId, entry);
    }
    return [...groups.entries()]
      .map(([issueId, g]) => ({ issueId, ...g }))
      .sort((a, b) => b.count - a.count);
  });

  /** Sprint from team.json: the configured activeSprint, falling back to the newest one. */
  readonly currentSprint = computed<TeamSprint | null>(() => {
    const team = this.team();
    const sprints = team?.sprints ?? [];
    if (sprints.length === 0) {
      return null;
    }
    const byName = team?.activeSprint
      ? sprints.find((s) => s.name === team.activeSprint)
      : undefined;
    const last = (s: TeamSprint) => [...s.workdays].sort().at(-1) ?? '';
    return byName ?? [...sprints].sort((a, b) => last(b).localeCompare(last(a)))[0];
  });

  readonly sprintDays = computed(() => {
    const sprint = this.currentSprint();
    if (!sprint || sprint.workdays.length === 0) {
      return null;
    }
    return {
      done: sprint.workdays.filter((d) => d <= this.today).length,
      total: sprint.workdays.length,
    };
  });

  /** "Sprint {name}, Tag {x}/{y}" for the header subline; null without team.json. */
  readonly sprintHeader = computed(() => {
    const sprint = this.currentSprint();
    const days = this.sprintDays();
    if (!sprint) {
      return null;
    }
    return days ? `Sprint ${sprint.name}, Tag ${days.done}/${days.total}` : `Sprint ${sprint.name}`;
  });

  /** Status distribution of my tickets (chips linking to /tickets). */
  readonly ticketStates = computed(() => {
    const counts = new Map<string, number>();
    for (const issue of this.issues()) {
      const state = issue.state ?? 'Ohne Status';
      counts.set(state, (counts.get(state) ?? 0) + 1);
    }
    return [...counts.entries()]
      .map(([state, count]) => ({ state, count }))
      .sort((a, b) => b.count - a.count);
  });

  /** Aggregated Ist/Plan over my tickets (parsed from the presentation strings). */
  readonly effortTotals = computed(() => {
    let spent = 0;
    let estimate = 0;
    for (const issue of this.issues()) {
      spent += parseDuration(issue.spent ?? '') ?? 0;
      estimate += parseDuration(issue.estimate ?? '') ?? 0;
    }
    return estimate > 0 ? { spent, estimate } : null;
  });

  readonly topTickets = computed(() => this.issues().slice(0, TOP_TICKET_COUNT));

  /** "Rest des Tages" quick chip: only with an open gap and a configured default ticket. */
  readonly restChipVisible = computed(
    () => this.todayGap() > 0 && this.settings.defaultIssueId() !== null,
  );

  /** Pause minutes rendered as duration string ("30m") for the presence input. */
  readonly pauseDisplay = computed(() => {
    const minutes = this.dayState.today().pauseMinutes;
    return minutes > 0 ? formatDuration(minutes) : '';
  });

  /** "±h:mm" saldo with typographic sign, or null while presence is not stamped. */
  readonly saldoLabel = computed(() => {
    const saldo = this.dayTarget.saldoMinutes();
    if (saldo === null) {
      return null;
    }
    return (saldo >= 0 ? '+' : '−') + formatClock(Math.abs(saldo));
  });

  /** Issue-id → web url for triage results and today's booking rows (own tickets + sprint pool). */
  private readonly webUrls = computed(
    () => new Map([...this.issues(), ...this.pool()].map((i) => [i.issueId, i.webUrl])),
  );

  constructor() {
    effect(() => {
      this.refresh.worklogVersion();
      this.dev.devParam();
      untracked(() => void this.load(false));
    });
    void this.api
      .getMeta()
      .then((meta) => this.calendarEnabled.set(meta.calendarEnabled))
      .catch(() => undefined);
    const monday = startOfWeek(new Date());
    void this.dayState.loadRange(toIsoDate(monday), toIsoDate(addDays(monday, 6)));
  }

  async load(refresh: boolean): Promise<void> {
    const devParam = this.dev.devParam();
    const monday = startOfWeek(new Date());
    const from = toIsoDate(monday);
    const to = toIsoDate(addDays(monday, 6));
    // Spinner only on first load — background reloads (after a booking) must not
    // tear down the list and its inline-book success chips.
    if (this.issues().length === 0) {
      this.loading.set(true);
    }
    this.error.set(null);
    try {
      const [issues, weekOverview, poolTasks, presets, team] = await Promise.all([
        this.api.getIssues(refresh, devParam),
        this.api.getOverview(from, to, refresh, devParam),
        this.api.getSprintPool(refresh, devParam),
        this.api.getPresets().catch(() => [] as BookingPreset[]),
        this.api.getTeam().catch(() => null),
      ]);
      this.issues.set(issues);
      this.week.set(weekOverview);
      this.pool.set(poolTasks);
      this.presets.set(presets);
      this.team.set(team);
      this.booking.prefetch(issues.slice(0, TOP_TICKET_COUNT)); // ↪ badge tooltips
    } catch (err) {
      this.error.set((err as Error).message);
    } finally {
      this.loading.set(false);
    }
  }

  // --- präsenz ---

  onComeChange(value: string): void {
    this.dayState.update(this.dayState.todayIso(), { come: value.trim() || null });
  }

  stampCome(): void {
    this.dayState.update(this.dayState.todayIso(), {
      come: formatWallClock(this.dayState.nowMinutes()),
    });
  }

  async onGoChange(value: string): Promise<void> {
    const go = value.trim();
    this.dayState.update(this.dayState.todayIso(), { go: go || null });
    if (parseClock(go) !== null) {
      await this.pauseTimerForGo(go);
    }
  }

  async stampGo(): Promise<void> {
    const go = formatWallClock(this.dayState.nowMinutes());
    this.dayState.update(this.dayState.todayIso(), { go });
    await this.pauseTimerForGo(go);
  }

  /** Leaving means stop working: a running timer is paused alongside the Gehe stamp. */
  private async pauseTimerForGo(go: string): Promise<void> {
    if (this.timer.state() && !this.timer.isPaused()) {
      try {
        await this.timer.pause();
        this.toast.show(`⏸ Timer pausiert — Gehe ${go} gesetzt`);
      } catch (err) {
        this.error.set((err as Error).message);
      }
    }
  }

  onPauseChange(value: string): void {
    const raw = value.trim();
    const minutes = raw === '' ? 0 : parseDuration(raw);
    if (minutes === null) {
      return; // invalid input — keep the stored pause
    }
    this.dayState.update(this.dayState.todayIso(), { pauseMinutes: minutes });
  }

  setPause(minutes: number): void {
    this.dayState.update(this.dayState.todayIso(), { pauseMinutes: minutes });
  }

  // --- booking ---

  async start(issue: TaskListItem): Promise<void> {
    this.error.set(null);
    try {
      await this.timer.start(issue.issueId, issue.summary);
    } catch (err) {
      this.error.set((err as Error).message);
    }
  }

  openLog(issue: TaskListItem): void {
    this.logDialog.set({ issueId: issue.issueId, issueSummary: issue.summary, minutes: null });
  }

  /** "+ Buchen": default ticket when configured, otherwise the dialog's pick-a-ticket mode. */
  openNewBooking(): void {
    this.logDialog.set({
      issueId: this.settings.defaultIssueId() ?? '',
      issueSummary: this.settings.defaultIssueSummary() ?? '',
      minutes: null,
    });
  }

  /** Gap pill: LogTimeDialog prefilled with the default ticket and the exact gap. */
  openGapBooking(): void {
    const gap = this.todayGap();
    if (gap <= 0) {
      return;
    }
    this.logDialog.set({
      issueId: this.settings.defaultIssueId() ?? '',
      issueSummary: this.settings.defaultIssueSummary() ?? '',
      minutes: gap,
    });
  }

  async bookPreset(preset: BookingPreset): Promise<void> {
    const minutes = this.settings.round(preset.minutes);
    await this.bookQuick(preset.id, {
      issueId: preset.issueId,
      date: this.today,
      minutes,
      typeId: preset.typeId,
      text: preset.comment,
    });
  }

  /** Books the exact remaining gap (deliberately NOT rounded) on the default ticket. */
  async bookRest(): Promise<void> {
    const issueId = this.settings.defaultIssueId();
    const minutes = this.todayGap();
    if (!issueId || minutes <= 0) {
      return;
    }
    await this.bookQuick('rest', {
      issueId,
      date: this.today,
      minutes,
      typeId: this.settings.defaultTypeId(),
      text: null,
    });
  }

  private async bookQuick(busyKey: string, request: WorkLogRequest): Promise<void> {
    this.bookingPresetId.set(busyKey);
    this.error.set(null);
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

  /** Two-step delete confirm, matching the topbar Verwerfen pattern (4s auto-disarm). */
  armDelete(item: WorkItem): void {
    this.confirmDeleteId.set(item.id);
    if (this.deleteResetHandle !== null) {
      clearTimeout(this.deleteResetHandle);
    }
    this.deleteResetHandle = setTimeout(() => this.confirmDeleteId.set(null), 4000);
  }

  async deleteItem(item: WorkItem): Promise<void> {
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

  private showBooked(issueId: string, minutes: number, redirectedFrom: string | null): void {
    const suffix = redirectedFrom ? ` (umgeleitet von ${redirectedFrom})` : '';
    this.toast.show(`${issueId} · ${formatDuration(minutes)} gebucht${suffix}`);
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

  /** "spent / estimate" mono label for a ticket row; amber when over the estimate. */
  effortFor(item: TaskListItem): { label: string; over: boolean } | null {
    if (!item.spent && !item.estimate) {
      return null;
    }
    const spent = parseDuration(item.spent ?? '') ?? 0;
    const estimate = parseDuration(item.estimate ?? '');
    return {
      label: `${item.spent ?? '0m'} / ${item.estimate ?? '–'}`,
      over: estimate !== null && spent > estimate,
    };
  }

  issueUrl(issueId: string): string | null {
    return this.webUrls().get(issueId) ?? null;
  }

  private heatFor(date: string, booked: number, target: number, isToday: boolean): string {
    if (isToday) {
      return 'today';
    }
    if (target === 0) {
      return 'off';
    }
    if (booked >= target) {
      return 'reached';
    }
    if (booked > 0) {
      return 'partial';
    }
    return date > this.today ? 'future' : 'none';
  }

  dayLetter(date: string): string {
    return formatDayLabel(date).slice(0, 2);
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
        case 'meetings':
          this.draftResult.set(await this.api.calendarDrafts(this.aiDate()));
          break;
        case 'gaps':
          this.draftResult.set(await this.api.aiGapfills(from, to, devParam));
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
