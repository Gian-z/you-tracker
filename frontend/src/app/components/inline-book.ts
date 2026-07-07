import { Component, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SubtaskChoice, SubtaskPickerDialog } from '../dialogs/subtask-picker-dialog';
import { formatDuration, parseDuration, toIsoDate } from '../format';
import { BookingTarget, TaskListItem, WorkItem, WorkLogRequest } from '../models';
import { BookingService } from '../services/booking.service';

/**
 * One-keystroke booking directly in a ticket-table row: type a duration
 * ("1h 30m", optionally "45m: Kommentar"), Enter books it for today with the
 * last-used work type. The task-redirect rule applies — a ✓ chip shows the
 * actual target when the booking landed on a subtask.
 */
@Component({
  selector: 'app-inline-book',
  imports: [FormsModule, SubtaskPickerDialog],
  template: `
    @if (success(); as s) {
      <span class="inline-book-success" [title]="s.title">✓ {{ s.text }}</span>
    } @else {
      <input
        type="text"
        class="inline-book-input"
        [class.invalid]="invalid()"
        [(ngModel)]="value"
        [disabled]="busy()"
        placeholder="1h 30m"
        autocomplete="off"
        [attr.aria-label]="'Zeit buchen auf ' + issue().issueId"
        [title]="errorMsg() ?? 'Dauer eingeben, Enter bucht für heute (optional: 45m: Kommentar)'"
        (keydown.enter)="book($event)"
        (keydown.escape)="clear()"
        (input)="invalid.set(false); errorMsg.set(null)"
      />
      @if (errorMsg(); as msg) {
        <span class="inline-book-error">{{ msg }}</span>
      }
      @if (busy()) {
        <span class="spinner"></span>
      }
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
export class InlineBook {
  private readonly booking = inject(BookingService);

  readonly issue = input.required<TaskListItem>();

  readonly value = signal('');
  readonly busy = signal(false);
  readonly invalid = signal(false);
  readonly errorMsg = signal<string | null>(null);
  readonly success = signal<{ text: string; title: string } | null>(null);
  readonly pickerTarget = signal<BookingTarget | null>(null);

  private pendingRequest: WorkLogRequest | null = null;

  clear(): void {
    this.value.set('');
    this.invalid.set(false);
    this.errorMsg.set(null);
  }

  async book(event: Event): Promise<void> {
    event.preventDefault();
    const raw = this.value().trim();
    if (!raw || this.busy()) {
      return;
    }
    // Optional comment shorthand: everything after the first ':' is the comment.
    const colon = raw.indexOf(':');
    const durationPart = colon >= 0 ? raw.slice(0, colon) : raw;
    const comment = colon >= 0 ? raw.slice(colon + 1).trim() : '';
    const minutes = parseDuration(durationPart);
    if (minutes === null) {
      this.invalid.set(true);
      this.errorMsg.set('Ungültige Dauer (z.B. 1h 30m, 90m, 1.5h)');
      return;
    }

    const request: WorkLogRequest = {
      issueId: this.issue().issueId,
      date: toIsoDate(new Date()),
      minutes,
      typeId: this.booking.lastTypeId,
      text: comment || null,
    };
    this.busy.set(true);
    try {
      const outcome = await this.booking.bookWithPolicy(request);
      if (outcome.status === 'needs-picker') {
        this.pendingRequest = request;
        this.pickerTarget.set(outcome.target);
      } else {
        this.showSuccess(outcome.item, minutes);
      }
    } catch (err) {
      this.invalid.set(true);
      this.errorMsg.set((err as Error).message);
    } finally {
      this.busy.set(false);
    }
  }

  async onPickerChosen(choice: SubtaskChoice): Promise<void> {
    const request = this.pendingRequest;
    this.pickerTarget.set(null);
    this.pendingRequest = null;
    if (!request) {
      return;
    }
    this.busy.set(true);
    try {
      const item = await this.booking.book({
        ...request,
        issueId: choice.issueId,
        allowFeature: choice.allowFeature,
      });
      this.showSuccess(item, request.minutes);
    } catch (err) {
      this.invalid.set(true);
      this.errorMsg.set((err as Error).message);
    } finally {
      this.busy.set(false);
    }
  }

  private showSuccess(item: WorkItem, minutes: number): void {
    const redirected = item.issueId !== this.issue().issueId;
    this.value.set('');
    this.success.set({
      text: redirected ? `${formatDuration(minutes)} → ${item.issueId}` : formatDuration(minutes),
      title: redirected
        ? `Gebucht auf ${item.issueId} (Task-Teilaufgabe von ${this.issue().issueId})`
        : `Gebucht auf ${item.issueId}`,
    });
    setTimeout(() => this.success.set(null), 4000);
  }
}
