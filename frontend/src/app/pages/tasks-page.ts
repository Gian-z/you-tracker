import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { InlineBook } from '../components/inline-book';
import { LogTimeDialog } from '../dialogs/log-time-dialog';
import { relativeTime } from '../format';
import { TaskListItem } from '../models';
import { ApiService } from '../services/api.service';
import { BookingService } from '../services/booking.service';
import { DevService } from '../services/dev.service';
import { RefreshService } from '../services/refresh.service';
import { TimerService } from '../services/timer.service';

@Component({
  selector: 'app-tasks-page',
  imports: [FormsModule, LogTimeDialog, InlineBook],
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
                @if (dev.isSelf()) {
                  <th>Buchen</th>
                }
                <th></th>
              </tr>
            </thead>
            <tbody>
              @for (t of filtered(); track t.issueId) {
                <tr>
                  <td class="nowrap">
                    <a [href]="t.webUrl" target="_blank" rel="noopener">{{ t.issueId }}</a>
                    @if (booking.isFeature(t)) {
                      <span class="redirect-badge" [title]="redirectHint(t)">↪</span>
                    }
                  </td>
                  <td class="summary-cell">{{ t.summary }}</td>
                  <td>{{ t.type ?? '–' }}</td>
                  <td>{{ t.state ?? '–' }}</td>
                  <td>{{ t.priority ?? '–' }}</td>
                  <td class="nowrap">{{ t.estimate ?? '–' }}</td>
                  <td class="nowrap">{{ t.spent ?? '–' }}</td>
                  <td class="nowrap muted">{{ rel(t.updated) }}</td>
                  @if (dev.isSelf()) {
                    <td class="nowrap inline-book-cell">
                      <app-inline-book [issue]="t" />
                    </td>
                  }
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
                  <td colspan="10" class="muted empty-cell">
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
  private readonly refresh = inject(RefreshService);
  protected readonly dev = inject(DevService);
  protected readonly booking = inject(BookingService);

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
    // Reloads on init, on dev change and after any booking (Spent column refresh —
    // the server-side issues: cache eviction makes even a non-refresh reload fresh).
    effect(() => {
      this.dev.devParam();
      this.refresh.worklogVersion();
      untracked(() => void this.load(false));
    });
  }

  async load(refresh: boolean): Promise<void> {
    // Spinner only on first load — background reloads (after a booking) must not
    // tear down the table and its inline-book success chips.
    if (this.issues().length === 0) {
      this.loading.set(true);
    }
    this.error.set(null);
    try {
      const issues = await this.api.getIssues(refresh, this.dev.devParam());
      this.issues.set(issues);
      this.booking.prefetch(issues);
    } catch (err) {
      this.error.set((err as Error).message);
    } finally {
      this.loading.set(false);
    }
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
