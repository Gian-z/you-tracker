import { Component, ElementRef, afterNextRender, inject, output, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LogTimeDialog } from './log-time-dialog';
import { TaskListItem } from '../models';
import { ApiService } from '../services/api.service';
import { TimerService } from '../services/timer.service';

const SEARCH_TOP = 25;
const DEBOUNCE_MS = 300;
const MIN_LENGTH = 2;

/**
 * Global YouTrack ticket search (Ctrl+K): finds tickets outside the configured
 * scope so time can be booked on them. Booking goes through the shared
 * LogTimeDialog, so the task-redirect rule applies unchanged.
 */
@Component({
  selector: 'app-issue-search-dialog',
  imports: [FormsModule, LogTimeDialog],
  host: { '(document:keydown.escape)': 'onEscape()' },
  template: `
    <div class="overlay" (click)="close()">
      <div class="dialog dialog-wide search-dialog" role="dialog" aria-label="Ticket-Suche" (click)="$event.stopPropagation()">
        <h2>Ticket-Suche</h2>
        <input
          #searchInput
          type="search"
          name="query"
          class="search-input"
          [ngModel]="query()"
          (ngModelChange)="onInput($event)"
          (keydown.enter)="searchNow()"
          placeholder="Ticket-ID (z.B. ST6-1234), Text oder YouTrack-Query…"
          autocomplete="off"
          aria-label="Suchbegriff"
        />

        @if (error(); as err) {
          <div class="banner error">
            {{ err }}
            <button type="button" class="link" (click)="error.set(null)">schliessen</button>
          </div>
        }

        @if (loading()) {
          <div class="loading"><span class="spinner"></span> Suche…</div>
        } @else if (query().trim().length < minLength) {
          <div class="muted small search-hint">Mindestens zwei Zeichen eingeben — durchsucht ganz YouTrack.</div>
        } @else if (searched() && results().length === 0) {
          <div class="muted small search-hint">Keine Treffer. YouTrack-Query-Syntax wird unterstützt.</div>
        } @else if (results().length > 0) {
          <div class="table-scroll search-results">
            <table class="data-table compact">
              <tbody>
                @for (t of results(); track t.issueId) {
                  <tr>
                    <td class="nowrap"><a [href]="t.webUrl" target="_blank" rel="noopener">{{ t.issueId }}</a></td>
                    <td class="summary-cell">{{ t.summary }}</td>
                    <td class="nowrap muted">{{ t.type ?? '–' }}</td>
                    <td class="nowrap">{{ t.state ?? '–' }}</td>
                    <td class="nowrap row-actions">
                      <button type="button" class="icon" title="Timer starten" (click)="startTimer(t)">▶</button>
                      <button type="button" class="icon" title="Zeit buchen" (click)="logIssue.set(t)">✎</button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          @if (results().length === searchTop) {
            <div class="muted small search-hint">Erste {{ searchTop }} Treffer — Suche verfeinern.</div>
          }
        }

        <div class="dialog-actions">
          <button type="button" class="secondary" (click)="close()">Schliessen</button>
        </div>
      </div>
    </div>

    @if (logIssue(); as issue) {
      <app-log-time-dialog
        [issueId]="issue.issueId"
        [issueSummary]="issue.summary"
        (saved)="onBooked()"
        (closed)="logIssue.set(null)"
      />
    }
  `,
})
export class IssueSearchDialog {
  private readonly api = inject(ApiService);
  private readonly timer = inject(TimerService);

  readonly closed = output<void>();

  protected readonly minLength = MIN_LENGTH;
  protected readonly searchTop = SEARCH_TOP;

  readonly query = signal('');
  readonly results = signal<TaskListItem[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly searched = signal(false);
  readonly logIssue = signal<TaskListItem | null>(null);

  private readonly searchInput = viewChild<ElementRef<HTMLInputElement>>('searchInput');
  private debounceHandle: ReturnType<typeof setTimeout> | null = null;
  private requestCounter = 0;

  constructor() {
    // `autofocus` is unreliable on dynamically inserted dialogs.
    afterNextRender(() => this.searchInput()?.nativeElement.focus());
  }

  onInput(value: string): void {
    this.query.set(value);
    if (this.debounceHandle !== null) {
      clearTimeout(this.debounceHandle);
    }
    this.debounceHandle = setTimeout(() => void this.search(), DEBOUNCE_MS);
  }

  searchNow(): void {
    if (this.debounceHandle !== null) {
      clearTimeout(this.debounceHandle);
      this.debounceHandle = null;
    }
    void this.search();
  }

  private async search(): Promise<void> {
    const text = this.query().trim();
    if (text.length < MIN_LENGTH) {
      this.results.set([]);
      this.searched.set(false);
      return;
    }
    const ticket = ++this.requestCounter;
    this.loading.set(true);
    this.error.set(null);
    try {
      const results = await this.api.searchIssues(text, SEARCH_TOP);
      if (ticket !== this.requestCounter) {
        return; // a newer search superseded this one
      }
      this.results.set(results);
      this.searched.set(true);
    } catch (err) {
      if (ticket === this.requestCounter) {
        this.error.set((err as Error).message);
      }
    } finally {
      if (ticket === this.requestCounter) {
        this.loading.set(false);
      }
    }
  }

  async startTimer(issue: TaskListItem): Promise<void> {
    try {
      await this.timer.start(issue.issueId, issue.summary);
      this.close();
    } catch (err) {
      this.error.set((err as Error).message);
    }
  }

  onBooked(): void {
    this.logIssue.set(null);
    this.close();
  }

  onEscape(): void {
    // The nested log dialog handles its own Escape; don't double-close.
    if (!this.logIssue()) {
      this.close();
    }
  }

  close(): void {
    this.closed.emit();
  }
}
