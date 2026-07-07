import { Component, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BookingTarget } from '../models';

export interface SubtaskChoice {
  issueId: string;
  /** True when the user explicitly books on the Feature itself (noTask case). */
  allowFeature: boolean;
}

/**
 * Resolves the interactive cases of the "bookings land on tasks" rule:
 * ambiguous → pick one of the task subtasks; noTask → confirm booking on the feature.
 */
@Component({
  selector: 'app-subtask-picker-dialog',
  imports: [FormsModule],
  host: { '(document:keydown.escape)': 'cancel()' },
  template: `
    <div class="overlay" (click)="cancel()">
      <div class="dialog" role="dialog" aria-label="Buchungsziel wählen" (click)="$event.stopPropagation()">
        <h2>Buchungsziel wählen</h2>
        <div class="dialog-issue">
          <span class="issue-id">{{ target().requestedIssueId }}</span>
          <span class="muted">{{ target().targetSummary }}</span>
        </div>

        @if (target().kind === 'ambiguous') {
          <p class="small muted">
            Dieses Feature hat mehrere Task-Teilaufgaben – auf welche soll gebucht werden?
          </p>
          @for (c of target().candidates; track c.issueId) {
            <label class="checkbox-row picker-option">
              <input
                type="radio"
                name="subtask"
                [value]="c.issueId"
                [checked]="selected() === c.issueId"
                (change)="selected.set(c.issueId)"
              />
              <span class="issue-id">{{ c.issueId }}</span>
              <span>{{ c.summary }}</span>
              @if (c.resolved) {
                <span class="muted small">(erledigt)</span>
              }
            </label>
          }
        } @else {
          <div class="banner error">
            {{ target().requestedIssueId }} ist ein Feature ohne Task-Teilaufgabe.
          </div>
          <p class="small muted">
            Buchungen sollen auf Tasks landen. Trotzdem auf das Feature buchen?
          </p>
        }

        <div class="dialog-actions">
          <button type="button" class="secondary" (click)="cancel()">Abbrechen</button>
          @if (target().kind === 'ambiguous') {
            <button type="button" class="primary" [disabled]="!selected()" (click)="confirmSubtask()">
              Buchen
            </button>
          } @else {
            <button type="button" class="primary" (click)="confirmFeature()">
              Auf Feature buchen
            </button>
          }
        </div>
      </div>
    </div>
  `,
})
export class SubtaskPickerDialog {
  readonly target = input.required<BookingTarget>();

  readonly chosen = output<SubtaskChoice>();
  readonly closed = output<void>();

  readonly selected = signal<string | null>(null);

  cancel(): void {
    this.closed.emit();
  }

  confirmSubtask(): void {
    const issueId = this.selected();
    if (issueId) {
      this.chosen.emit({ issueId, allowFeature: false });
    }
  }

  confirmFeature(): void {
    this.chosen.emit({ issueId: this.target().requestedIssueId, allowFeature: true });
  }
}
