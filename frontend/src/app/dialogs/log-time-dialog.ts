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
import { formatDuration, parseDuration, toIsoDate } from '../format';
import { BookingTarget, TaskListItem, WorkItem, WorkType } from '../models';
import { ApiService } from '../services/api.service';
import { BookingService } from '../services/booking.service';
import { TodayStatusService } from '../services/today-status.service';
import { ToastService } from '../services/toast.service';

@Component({
  selector: 'app-log-time-dialog',
  imports: [FormsModule],
  host: { '(document:keydown.escape)': 'cancel()' },
  template: `
    <div class="overlay" (click)="cancel()">
      <div class="dialog" role="dialog" aria-label="Zeit buchen" (click)="$event.stopPropagation()">
        <h2>Zeit buchen</h2>
        @if (pickIssue()) {
          <label>
            Ticket
            <select name="pickedIssue" [(ngModel)]="pickedIssueId" required>
              <option value="" disabled>Ticket wählen…</option>
              @for (i of ownIssues(); track i.issueId) {
                <option [value]="i.issueId">{{ i.issueId }} – {{ i.summary }}</option>
              }
            </select>
          </label>
        } @else {
          <div class="dialog-issue">
            <span class="issue-id">{{ issueId() }}</span>
            <span class="muted">{{ issueSummary() }}</span>
          </div>
        }

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
          @if (error(); as err) {
            <div class="banner error">{{ err }}</div>
          }
          <div class="dialog-actions">
            <button type="button" class="secondary" (click)="cancel()" [disabled]="saving()">Abbrechen</button>
            <button type="submit" class="primary" [disabled]="saving() || !targetReady()">
              @if (saving()) {
                <span class="spinner"></span>
              }
              Buchen
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

  /** Empty string switches the dialog into pick-a-ticket mode (e.g. per-day booking). */
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

  /** Pick-a-ticket mode (no fixed issue passed in). */
  readonly pickIssue = computed(() => this.issueId() === '');
  readonly ownIssues = signal<TaskListItem[]>([]);
  readonly pickedIssueId = signal('');

  readonly currentIssueId = computed(() =>
    this.pickIssue() ? this.pickedIssueId() : this.issueId(),
  );

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
    effect(() => {
      const issueId = this.currentIssueId();
      untracked(() => {
        this.target.set(null);
        this.chosenTargetId.set('');
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
    effect(() => {
      if (this.pickIssue()) {
        untracked(() =>
          void this.api
            .getIssues()
            .then((issues) => this.ownIssues.set(issues))
            .catch(() => undefined),
        );
      }
    });
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

  cancel(): void {
    if (!this.saving()) {
      this.closed.emit();
    }
  }

  private currentSummary(): string {
    if (!this.pickIssue()) {
      return this.issueSummary();
    }
    return this.ownIssues().find((i) => i.issueId === this.pickedIssueId())?.summary ?? '';
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
