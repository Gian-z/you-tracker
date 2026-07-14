import {
  Component,
  ElementRef,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TicketPicker } from '../components/ticket-picker';
import { formatClock, formatDayLabel, formatDuration, parseDuration, toIsoDate } from '../format';
import { BookingTarget, TaskListItem, WorkItem, WorkType } from '../models';
import { ApiService } from '../services/api.service';
import { BookingService } from '../services/booking.service';
import { DayTargetService } from '../services/day-target.service';
import { TodayStatusService } from '../services/today-status.service';
import { ToastService } from '../services/toast.service';

@Component({
  selector: 'app-log-time-dialog',
  imports: [FormsModule, TicketPicker],
  host: { '(document:keydown.escape)': 'cancel()' },
  template: `
    <div class="overlay" (click)="cancel()">
      <div class="dialog" role="dialog" aria-label="Zeit buchen" (click)="$event.stopPropagation()">
        <div class="dialog-head-row">
          <h2>Zeit buchen</h2>
          <button type="button" class="icon" (click)="cancel()" aria-label="Schliessen">✕</button>
        </div>
        <div class="dialog-issue" style="display: flex; align-items: center; gap: 0.5rem; min-width: 0;">
          <app-ticket-picker
            [issueId]="currentIssueId() || null"
            [issueSummary]="currentSummary()"
            [placeholder]="'Ticket wählen'"
            (picked)="onTicketPicked($event)"
          />
          <span class="muted small" style="flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">
            {{ currentSummary() }}
          </span>
          <span class="muted small" style="white-space: nowrap;">{{ dateLabel() }}</span>
        </div>

        @if (target(); as t) {
          @if (t.kind === 'redirected') {
            <div class="banner info">
              ↪ Wird gebucht auf <strong>{{ t.targetIssueId }}</strong> – {{ t.targetSummary }}
              @if (t.targetResolved) {
                <span class="muted">(erledigt)</span>
              }
            </div>
          } @else if (t.kind === 'noTask') {
            <div class="banner error">
              Feature ohne Task-Teilaufgabe – Buchungen sollen auf Tasks landen.
            </div>
          }
        }

        <form (ngSubmit)="save()">
          @if (target()?.kind === 'ambiguous') {
            <label>
              Buchen auf (Task-Teilaufgabe)
              <select name="targetIssue" [(ngModel)]="chosenTargetId" required>
                <option value="" disabled>Task wählen…</option>
                @for (c of target()!.candidates; track c.issueId) {
                  <option [value]="c.issueId">
                    {{ c.issueId }} – {{ c.summary }}{{ c.resolved ? ' (erledigt)' : '' }}
                  </option>
                }
              </select>
            </label>
          }
          <label>
            Dauer
            <input
              #durationInput
              type="text"
              name="duration"
              [(ngModel)]="duration"
              placeholder="1h 30m, 90m, 1.5h oder 90"
              autocomplete="off"
            />
          </label>
          <div class="duration-chips">
            @for (chip of durationChips(); track chip.label) {
              <button type="button" class="duration-chip" (click)="duration.set(chip.value)">
                {{ chip.label }}
              </button>
            }
          </div>
          <label>
            Datum
            <input type="date" name="date" [(ngModel)]="date" required />
          </label>
          <label>
            Typ
            <select name="typeId" [(ngModel)]="typeId">
              <option value="">(keiner)</option>
              @for (t of workTypes(); track t.id) {
                <option [value]="t.id">{{ t.name }}</option>
              }
            </select>
          </label>
          <label>
            Kommentar
            <input type="text" name="comment" [(ngModel)]="comment" placeholder="Woran gearbeitet?" autocomplete="off" />
          </label>
          @if (target()?.kind === 'noTask') {
            <label class="checkbox-row">
              <input type="checkbox" name="allowFeature" [(ngModel)]="allowFeature" />
              Trotzdem auf das Feature buchen
            </label>
          }
          <label class="checkbox-row">
            <input type="checkbox" name="saveAsPreset" [(ngModel)]="saveAsPreset" />
            Als Preset speichern (wiederkehrende Buchung, z.B. Daily)
          </label>
          @if (saveAsPreset()) {
            <label>
              Preset-Name
              <input type="text" name="presetName" [(ngModel)]="presetName" placeholder="z.B. Daily Standup" autocomplete="off" />
            </label>
          }
          @if (overSollMinutes() > 0) {
            <div class="banner" style="background: var(--amber-bg); border-color: var(--amber-border); color: var(--amber);">
              ⚠ Übersteigt dein heutiges Soll um {{ overSollLabel() }} — trotzdem möglich.
            </div>
          }
          @if (error(); as err) {
            <div class="banner error">{{ err }}</div>
          }
          <div class="dialog-actions">
            <button type="button" class="secondary" (click)="cancel()" [disabled]="saving()">Abbrechen</button>
            <button
              type="submit"
              class="primary"
              [disabled]="saving() || !targetReady()"
              style="background: var(--green-strong); border-color: var(--green-strong); color: #08140b;"
            >
              @if (saving()) {
                <span class="spinner"></span>
              }
              {{ saveLabel() }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `,
})
export class LogTimeDialog {
  private readonly api = inject(ApiService);
  private readonly booking = inject(BookingService);
  private readonly todayStatus = inject(TodayStatusService);
  private readonly dayTarget = inject(DayTargetService);
  private readonly toast = inject(ToastService);

  private readonly durationInput = viewChild<ElementRef<HTMLInputElement>>('durationInput');

  /** 15m…4h plus "Rest des Tages" when a gap exists and the dialog books for today. */
  readonly durationChips = computed(() => {
    const chips = ['15m', '30m', '1h', '2h', '4h'].map((v) => ({ label: v, value: v }));
    const gap = this.todayStatus.gapMinutes();
    if (gap > 0 && this.date() === toIsoDate(new Date())) {
      chips.push({ label: `Rest des Tages (${formatDuration(gap)})`, value: formatDuration(gap) });
    }
    return chips;
  });

  /** Empty string starts the dialog without a ticket — the TicketPicker chip chooses one. */
  readonly issueId = input.required<string>();
  readonly issueSummary = input<string>('');
  readonly initialMinutes = input<number | null>(null);
  readonly initialDate = input<string | null>(null);

  readonly closed = output<void>();
  readonly saved = output<WorkItem>();

  readonly duration = signal('');
  readonly date = signal(toIsoDate(new Date()));
  readonly typeId = signal('');
  readonly comment = signal('');
  readonly workTypes = signal<WorkType[]>([]);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly saveAsPreset = signal(false);
  readonly presetName = signal('');
  readonly allowFeature = signal(false);

  /** Task-redirect pre-flight result; null while loading (server safety net covers the race). */
  readonly target = signal<BookingTarget | null>(null);
  readonly chosenTargetId = signal('');

  /** Ticket picked via the chip — overrides the issue passed in. */
  readonly picked = signal<TaskListItem | null>(null);

  readonly currentIssueId = computed(() => this.picked()?.issueId ?? this.issueId());
  readonly currentSummary = computed(() => this.picked()?.summary ?? this.issueSummary());

  readonly dateLabel = computed(() => formatDayLabel(this.date()));

  /** "{dur} buchen" as soon as the duration parses (mockup save button). */
  readonly saveLabel = computed(() => {
    const minutes = parseDuration(this.duration());
    return minutes === null ? 'Buchen' : `${formatDuration(minutes)} buchen`;
  });

  /** New booking for today exceeding the day target → amber warning (never blocks). */
  readonly overSollMinutes = computed(() => {
    if (this.date() !== toIsoDate(new Date())) {
      return 0;
    }
    const minutes = parseDuration(this.duration());
    if (minutes === null) {
      return 0;
    }
    return Math.max(0, this.todayStatus.bookedMinutes() + minutes - this.dayTarget.targetToday());
  });

  readonly overSollLabel = computed(() => formatClock(this.overSollMinutes()));

  /** Ambiguous targets need an explicit choice; noTask needs the explicit checkbox. */
  readonly targetReady = computed(() => {
    if (this.currentIssueId() === '') {
      return false;
    }
    const t = this.target();
    if (t?.kind === 'ambiguous') {
      return this.chosenTargetId() !== '';
    }
    if (t?.kind === 'noTask') {
      return this.allowFeature();
    }
    return true;
  });

  constructor() {
    effect(() => {
      const minutes = this.initialMinutes();
      if (minutes !== null) {
        this.duration.set(formatDuration(Math.max(1, minutes)));
      }
      const date = this.initialDate();
      if (date) {
        this.date.set(date);
      }
    });
    // Re-run the booking-target pre-flight whenever the ticket changes (chip pick included).
    effect(() => {
      const issueId = this.currentIssueId();
      untracked(() => {
        this.target.set(null);
        this.chosenTargetId.set('');
        this.allowFeature.set(false);
        if (issueId === '') {
          return;
        }
        void this.booking
          .resolve(issueId)
          .then((target) => this.target.set(target))
          .catch(() => undefined); // no pre-flight — the command handler still enforces the rule
      });
    });
    // `autofocus` is unreliable on dynamically inserted dialogs.
    afterNextRender(() => this.durationInput()?.nativeElement.focus());
    this.api
      .getWorkTypes()
      .then((types) => {
        this.workTypes.set(types);
        const last = this.booking.lastTypeId;
        if (!this.typeId() && last && types.some((t) => t.id === last)) {
          this.typeId.set(last);
        }
      })
      .catch((err: Error) => this.error.set(`Arbeitstypen konnten nicht geladen werden: ${err.message}`));
  }

  onTicketPicked(item: TaskListItem): void {
    this.picked.set(item);
  }

  cancel(): void {
    if (!this.saving()) {
      this.closed.emit();
    }
  }

  /** Where the booking actually lands, after redirect/picker/confirmation. */
  private effectiveTarget(): { issueId: string; summary: string; allowFeature: boolean } {
    const t = this.target();
    if (t?.kind === 'redirected') {
      return { issueId: t.targetIssueId, summary: t.targetSummary ?? '', allowFeature: false };
    }
    if (t?.kind === 'ambiguous' && this.chosenTargetId()) {
      const chosen = t.candidates.find((c) => c.issueId === this.chosenTargetId());
      return { issueId: this.chosenTargetId(), summary: chosen?.summary ?? '', allowFeature: false };
    }
    if (t?.kind === 'noTask') {
      return { issueId: this.currentIssueId(), summary: this.currentSummary(), allowFeature: true };
    }
    return { issueId: this.currentIssueId(), summary: this.currentSummary(), allowFeature: false };
  }

  async save(): Promise<void> {
    const minutes = parseDuration(this.duration());
    if (minutes === null) {
      this.error.set('Ungültige Dauer. Z.B. "1h 30m", "90m", "1.5h" oder Minuten.');
      return;
    }
    if (!this.date()) {
      this.error.set('Bitte ein Datum wählen.');
      return;
    }
    if (!this.targetReady()) {
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    try {
      const typeId = this.typeId() || null;
      const text = this.comment().trim() || null;
      const target = this.effectiveTarget();
      const item = await this.booking.book({
        issueId: target.issueId,
        date: this.date(),
        minutes,
        typeId,
        text,
        allowFeature: target.allowFeature,
      });
      if (this.saveAsPreset()) {
        const typeName = this.workTypes().find((t) => t.id === typeId)?.name ?? null;
        // Presets store the resolved target so booking them skips the redirect entirely.
        await this.api.savePreset({
          name: this.presetName().trim() || `${target.issueId} (${minutes}m)`,
          issueId: target.issueId,
          issueSummary: target.summary,
          minutes,
          typeId,
          typeName,
          comment: text,
        });
      }
      this.toast.show(`${item.issueId} · ${formatDuration(minutes)} gebucht`);
      this.saved.emit(item);
      this.closed.emit();
    } catch (err) {
      this.error.set((err as Error).message);
    } finally {
      this.saving.set(false);
    }
  }
}
