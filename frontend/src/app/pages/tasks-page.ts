import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LogTimeDialog } from '../dialogs/log-time-dialog';
import { relativeTime } from '../format';
import { TaskListItem } from '../models';
import { ApiService } from '../services/api.service';
import { DevService } from '../services/dev.service';
import { TimerService } from '../services/timer.service';

@Component({
  selector: 'app-tasks-page',
  imports: [FormsModule, LogTimeDialog],
  template: `
    <div class="page">
      <div class="toolbar">
        <input
          type="search"
          class="filter-input"
          placeholder="Filter by id, summary, state…"
          [(ngModel)]="filter"
          aria-label="Filter tasks"
        />
        <button type="button" (click)="load(true)" [disabled]="loading()">Refresh</button>
        <span class="muted">{{ filtered().length }} of {{ issues().length }}</span>
      </div>

      @if (error(); as err) {
        <div class="banner error">
          {{ err }}
          <button type="button" class="link" (click)="error.set(null)">dismiss</button>
        </div>
      }

      @if (loading()) {
        <div class="loading"><span class="spinner"></span> Loading issues…</div>
      } @else {
        <div class="table-scroll">
          <table class="data-table">
            <thead>
              <tr>
                <th>ID</th>
                <th>Summary</th>
                <th>Type</th>
                <th>State</th>
                <th>Priority</th>
                <th>Est</th>
                <th>Spent</th>
                <th>Updated</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              @for (t of filtered(); track t.issueId) {
                <tr>
                  <td class="nowrap">
                    <a [href]="t.webUrl" target="_blank" rel="noopener">{{ t.issueId }}</a>
                  </td>
                  <td class="summary-cell">{{ t.summary }}</td>
                  <td>{{ t.type ?? '–' }}</td>
                  <td>{{ t.state ?? '–' }}</td>
                  <td>{{ t.priority ?? '–' }}</td>
                  <td class="nowrap">{{ t.estimate ?? '–' }}</td>
                  <td class="nowrap">{{ t.spent ?? '–' }}</td>
                  <td class="nowrap muted">{{ rel(t.updated) }}</td>
                  <td class="nowrap row-actions">
                    @if (dev.isSelf()) {
                      <button type="button" class="icon" title="Start timer" (click)="start(t)">▶</button>
                      <button type="button" class="icon" title="Log time" (click)="logIssue.set(t)">✎</button>
                    } @else {
                      <span class="muted" title="Bookings are always created as you — switch back to yourself to book">read-only</span>
                    }
                  </td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="9" class="muted empty-cell">
                    @if (issues().length === 0) {
                      No issues found.
                    } @else {
                      No issues match the filter.
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>

    @if (logIssue(); as issue) {
      <app-log-time-dialog
        [issueId]="issue.issueId"
        [issueSummary]="issue.summary"
        (closed)="logIssue.set(null)"
      />
    }
  `,
})
export class TasksPage {
  private readonly api = inject(ApiService);
  private readonly timer = inject(TimerService);
  protected readonly dev = inject(DevService);

  readonly issues = signal<TaskListItem[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly filter = signal('');
  readonly logIssue = signal<TaskListItem | null>(null);

  readonly filtered = computed(() => {
    const query = this.filter().trim().toLowerCase();
    if (!query) {
      return this.issues();
    }
    return this.issues().filter(
      (t) =>
        t.issueId.toLowerCase().includes(query) ||
        t.summary.toLowerCase().includes(query) ||
        (t.state ?? '').toLowerCase().includes(query),
    );
  });

  constructor() {
    // Reloads on init and whenever the selected dev changes.
    effect(() => {
      this.dev.devParam();
      void this.load(false);
    });
  }

  async load(refresh: boolean): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.issues.set(await this.api.getIssues(refresh, this.dev.devParam()));
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

  rel(iso: string): string {
    return relativeTime(iso);
  }
}
