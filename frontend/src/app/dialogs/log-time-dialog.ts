import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { formatDuration, parseDuration, toIsoDate } from '../format';
import { BookingTarget, WorkItem, WorkType } from '../models';
import { ApiService } from '../services/api.service';
import { BookingService } from '../services/booking.service';

@Component({
  selector: 'app-log-time-dialog',
  imports: [FormsModule],
  host: { '(document:keydown.escape)': 'cancel()' },
  template: `
    <div class="overlay" (click)="cancel()">
      <div class="dialog" role="dialog" aria-label="Zeit buchen" (click)="$event.stopPropagation()">
        <h2>Zeit buchen</h2>
        <div class="dialog-issue">
          <span class="issue-id">{{ issueId() }}</span>
          <span class="muted">{{ issueSummary() }}</span>
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
              type="text"
              name="duration"
              [(ngModel)]="duration"
              placeholder="1h 30m, 90m, 1.5h oder 90"
              autocomplete="off"
              autofocus
            />
          </label>
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

  /** Ambiguous targets need an explicit choice; noTask needs the explicit checkbox. */
  readonly targetReady = computed(() => {
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
      const issueId = this.issueId();
      untracked(() => {
        this.target.set(null);
        this.chosenTargetId.set('');
        void this.booking
          .resolve(issueId)
          .then((target) => this.target.set(target))
          .catch(() => undefined); // no pre-flight — the command handler still enforces the rule
      });
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
      return { issueId: this.issueId(), summary: this.issueSummary(), allowFeature: true };
    }
    return { issueId: this.issueId(), summary: this.issueSummary(), allowFeature: false };
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
      this.saved.emit(item);
      this.closed.emit();
    } catch (err) {
      this.error.set((err as Error).message);
    } finally {
      this.saving.set(false);
    }
  }
}
