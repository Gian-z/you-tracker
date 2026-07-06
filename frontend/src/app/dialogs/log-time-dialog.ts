import { Component, effect, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { formatDuration, parseDuration, toIsoDate } from '../format';
import { WorkItem, WorkType } from '../models';
import { ApiService } from '../services/api.service';
import { RefreshService } from '../services/refresh.service';

@Component({
  selector: 'app-log-time-dialog',
  imports: [FormsModule],
  host: { '(document:keydown.escape)': 'cancel()' },
  template: `
    <div class="overlay" (click)="cancel()">
      <div class="dialog" role="dialog" aria-label="Log time" (click)="$event.stopPropagation()">
        <h2>Log time</h2>
        <div class="dialog-issue">
          <span class="issue-id">{{ issueId() }}</span>
          <span class="muted">{{ issueSummary() }}</span>
        </div>
        <form (ngSubmit)="save()">
          <label>
            Duration
            <input
              type="text"
              name="duration"
              [(ngModel)]="duration"
              placeholder="1h 30m, 90m, 1.5h or 90"
              autocomplete="off"
              autofocus
            />
          </label>
          <label>
            Date
            <input type="date" name="date" [(ngModel)]="date" required />
          </label>
          <label>
            Type
            <select name="typeId" [(ngModel)]="typeId">
              <option value="">(none)</option>
              @for (t of workTypes(); track t.id) {
                <option [value]="t.id">{{ t.name }}</option>
              }
            </select>
          </label>
          <label>
            Comment
            <input type="text" name="comment" [(ngModel)]="comment" placeholder="What did you do?" autocomplete="off" />
          </label>
          <label class="checkbox-row">
            <input type="checkbox" name="saveAsPreset" [(ngModel)]="saveAsPreset" />
            Save as preset (recurring booking, e.g. daily standup)
          </label>
          @if (saveAsPreset()) {
            <label>
              Preset name
              <input type="text" name="presetName" [(ngModel)]="presetName" placeholder="e.g. Daily Standup" autocomplete="off" />
            </label>
          }
          @if (error(); as err) {
            <div class="banner error">{{ err }}</div>
          }
          <div class="dialog-actions">
            <button type="button" class="secondary" (click)="cancel()" [disabled]="saving()">Cancel</button>
            <button type="submit" class="primary" [disabled]="saving()">
              @if (saving()) {
                <span class="spinner"></span>
              }
              Save
            </button>
          </div>
        </form>
      </div>
    </div>
  `,
})
export class LogTimeDialog {
  private readonly api = inject(ApiService);
  private readonly refresh = inject(RefreshService);

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
    this.api
      .getWorkTypes()
      .then((types) => this.workTypes.set(types))
      .catch((err: Error) => this.error.set(`Could not load work types: ${err.message}`));
  }

  cancel(): void {
    if (!this.saving()) {
      this.closed.emit();
    }
  }

  async save(): Promise<void> {
    const minutes = parseDuration(this.duration());
    if (minutes === null) {
      this.error.set('Invalid duration. Try "1h 30m", "90m", "1.5h" or plain minutes.');
      return;
    }
    if (!this.date()) {
      this.error.set('Please pick a date.');
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    try {
      const typeId = this.typeId() || null;
      const text = this.comment().trim() || null;
      const item = await this.api.createWorklog({
        issueId: this.issueId(),
        date: this.date(),
        minutes,
        typeId,
        text,
      });
      if (this.saveAsPreset()) {
        const typeName = this.workTypes().find((t) => t.id === typeId)?.name ?? null;
        await this.api.savePreset({
          name: this.presetName().trim() || `${this.issueId()} (${minutes}m)`,
          issueId: this.issueId(),
          issueSummary: this.issueSummary(),
          minutes,
          typeId,
          typeName,
          comment: text,
        });
      }
      this.refresh.worklogChanged();
      this.saved.emit(item);
      this.closed.emit();
    } catch (err) {
      this.error.set((err as Error).message);
    } finally {
      this.saving.set(false);
    }
  }
}
