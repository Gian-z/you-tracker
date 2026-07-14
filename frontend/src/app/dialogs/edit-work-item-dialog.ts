import {
  Component,
  ElementRef,
  afterNextRender,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { formatDuration, parseDuration } from '../format';
import { WorkItem, WorkType } from '../models';
import { ApiService } from '../services/api.service';
import { BookingService } from '../services/booking.service';
import { ToastService } from '../services/toast.service';

const DURATION_CHIPS = ['15m', '30m', '1h', '2h', '4h'];

/**
 * Lean edit dialog for an existing booking (duration/date/type/comment).
 * Deliberately NOT a third mode of LogTimeDialog — no target redirect, no presets,
 * no issue picking; moving a booking to another issue = delete + re-book.
 */
@Component({
  selector: 'app-edit-work-item-dialog',
  imports: [FormsModule],
  host: { '(document:keydown.escape)': 'cancel()' },
  template: `
    <div class="overlay" (click)="cancel()">
      <div class="dialog" role="dialog" aria-label="Buchung bearbeiten" (click)="$event.stopPropagation()">
        <div class="dialog-head-row">
          <h2>Buchung bearbeiten</h2>
          <button type="button" class="icon" (click)="cancel()" aria-label="Schliessen">✕</button>
        </div>
        <!-- Deliberately no TicketPicker here: moving a booking to another issue = delete + re-book. -->
        <div class="dialog-issue">
          <span class="issue-id">{{ item().issueId }}</span>
          <span class="muted">{{ item().issueSummary }}</span>
        </div>
        <form (ngSubmit)="save()">
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
            @for (chip of chips; track chip) {
              <button type="button" class="duration-chip" (click)="duration.set(chip)">{{ chip }}</button>
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
            <input type="text" name="comment" [(ngModel)]="comment" autocomplete="off" />
          </label>
          @if (error(); as err) {
            <div class="banner error">{{ err }}</div>
          }
          <div class="dialog-actions">
            <button type="button" class="secondary" (click)="cancel()" [disabled]="saving()">Abbrechen</button>
            <button
              type="submit"
              class="primary"
              [disabled]="saving()"
              style="background: var(--green-strong); border-color: var(--green-strong); color: #08140b;"
            >
              @if (saving()) {
                <span class="spinner"></span>
              }
              Speichern
            </button>
          </div>
        </form>
      </div>
    </div>
  `,
})
export class EditWorkItemDialog {
  private readonly api = inject(ApiService);
  private readonly booking = inject(BookingService);
  private readonly toast = inject(ToastService);

  readonly item = input.required<WorkItem>();

  readonly closed = output<void>();
  readonly saved = output<WorkItem>();

  protected readonly chips = DURATION_CHIPS;

  readonly duration = signal('');
  readonly date = signal('');
  readonly typeId = signal('');
  readonly comment = signal('');
  readonly workTypes = signal<WorkType[]>([]);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  private readonly durationInput = viewChild<ElementRef<HTMLInputElement>>('durationInput');

  constructor() {
    effect(() => {
      const item = this.item();
      untracked(() => {
        this.duration.set(formatDuration(item.minutes));
        this.date.set(item.date);
        this.typeId.set(item.typeId ?? '');
        this.comment.set(item.text ?? '');
      });
    });
    this.api
      .getWorkTypes()
      .then((types) => this.workTypes.set(types))
      .catch(() => undefined); // select degrades to "(keiner)" + current value
    afterNextRender(() => this.durationInput()?.nativeElement.focus());
  }

  cancel(): void {
    if (!this.saving()) {
      this.closed.emit();
    }
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
    this.saving.set(true);
    this.error.set(null);
    try {
      const updated = await this.booking.update(this.item(), {
        date: this.date(),
        minutes,
        typeId: this.typeId() || null,
        text: this.comment().trim() || null,
      });
      this.toast.show(`${updated.issueId} · ${formatDuration(minutes)} aktualisiert`);
      this.saved.emit(updated);
      this.closed.emit();
    } catch (err) {
      this.error.set((err as Error).message);
    } finally {
      this.saving.set(false);
    }
  }
}
