import { Component, ElementRef, afterNextRender, computed, inject, output, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LogTimeDialog } from './log-time-dialog';
import { formatDuration, parseDuration, toIsoDate } from '../format';
import { TaskListItem } from '../models';
import { ApiService } from '../services/api.service';
import { BookingService } from '../services/booking.service';
import { SettingsService } from '../services/settings.service';
import { TimerService } from '../services/timer.service';
import { ToastService } from '../services/toast.service';

const SEARCH_TOP = 25;
const DEBOUNCE_MS = 300;
const MIN_LENGTH = 2;

/** "45m ST6-124 Kommentar" — duration, issue id, optional comment. */
const BOOK_PATTERN = /^(\S+)\s+([A-Za-z][A-Za-z0-9]*-\d+)(?:\s+(.*))?$/;

interface BookShorthand {
  minutes: number;
  issueId: string;
  comment: string;
}

/**
 * Global YouTrack ticket search (Ctrl+K): finds tickets outside the configured
 * scope so time can be booked on them. Booking goes through the shared
 * LogTimeDialog, so the task-redirect rule applies unchanged. The palette also
 * accepts the direct-booking shorthand "45m ST6-124 Kommentar" (Enter books today).
 */
@Component({
  selector: 'app-issue-search-dialog',
  imports: [FormsModule, LogTimeDialog],
  host: { '(document:keydown.escape)': 'onEscape()' },
  template: `
    <div class="overlay" (click)="close()">
      <div class="dialog dialog-wide search-dialog" role="dialog" aria-label="Ticket-Suche" (click)="$event.stopPropagation()">
        <div class="dialog-head-row">
          <h2>Ticket-Suche</h2>
          <button type="button" class="icon" (click)="close()" aria-label="Schliessen">✕</button>
        </div>
        <input
          #searchInput
          type="search"
          name="query"
          class="search-input"
          [ngModel]="query()"
          (ngModelChange)="onInput($event)"
          (keydown.enter)="onEnter()"
          placeholder="Suchen… oder buchen: 45m ST6-124 Kommentar"
          autocomplete="off"
          aria-label="Suchbegriff"
        />

        @if (error(); as err) {
          <div class="banner error">
            {{ err }}
            <button type="button" class="link" (click)="error.set(null)">schliessen</button>
          </div>
        }

        @if (bookShorthand(); as book) {
          <div class="banner success">
            ↵ Enter bucht:
            <span class="num"
              >{{ formatDuration(book.minutes) }} auf {{ book.issueId }}{{ book.comment ? ' — «' + book.comment + '»' : '' }}</span
            >
            @if (booking()) {
              <span class="spinner"></span>
            }
          </div>
        } @else if (loading()) {
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
                  <tr (click)="logIssue.set(t)" style="cursor: pointer;">
                    <td class="nowrap">
                      <a [href]="t.webUrl" target="_blank" rel="noopener" (click)="$event.stopPropagation()">{{ t.issueId }}</a>
                    </td>
                    <td class="summary-cell">{{ t.summary }}</td>
                    <td class="nowrap muted">{{ t.type ?? '–' }}</td>
                    <td class="nowrap">{{ t.state ?? '–' }}</td>
                    <td class="nowrap row-actions">
                      <button type="button" class="icon" title="Timer starten" (click)="$event.stopPropagation(); startTimer(t)">▶</button>
                      <button type="button" class="icon" title="Zeit buchen" (click)="$event.stopPropagation(); logIssue.set(t)">✎</button>
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

        <div class="muted small search-hint">
          Klick auf Ticket öffnet Buchung · <span class="num">45m ST6-124 Daily</span> bucht direkt · Esc schliesst
        </div>
      </div>
    </div>

    @if (logIssue(); as issue) {
      <app-log-time-dialog
        [issueId]="issue.issueId"
        [issueSummary]="issue.summary"
        [initialMinutes]="prefillMinutes()"
        (saved)="onBooked()"
        (closed)="logIssue.set(null); prefillMinutes.set(null)"
      />
    }
  `,
})
export class IssueSearchDialog {
  private readonly api = inject(ApiService);
  private readonly timer = inject(TimerService);
  private readonly bookingService = inject(BookingService);
  private readonly settings = inject(SettingsService);
  private readonly toast = inject(ToastService);

  readonly closed = output<void>();

  protected readonly minLength = MIN_LENGTH;
  protected readonly searchTop = SEARCH_TOP;
  protected readonly formatDuration = formatDuration;

  readonly query = signal('');
  readonly results = signal<TaskListItem[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly searched = signal(false);
  readonly logIssue = signal<TaskListItem | null>(null);
  readonly booking = signal(false);
  /** Duration prefill when the shorthand falls back to the nested log dialog. */
  readonly prefillMinutes = signal<number | null>(null);

  /** Direct-booking shorthand match; non-null suppresses search-as-you-type. */
  readonly bookShorthand = computed<BookShorthand | null>(() => {
    const match = this.query().trim().match(BOOK_PATTERN);
    if (!match) {
      return null;
    }
    const minutes = parseDuration(match[1]);
    if (minutes === null) {
      return null;
    }
    return { minutes, issueId: match[2].toUpperCase(), comment: match[3]?.trim() ?? '' };
  });

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
      this.debounceHandle = null;
    }
    if (this.bookShorthand()) {
      // Shorthand active — show the booking hint instead of searching.
      this.results.set([]);
      this.searched.set(false);
      return;
    }
    this.debounceHandle = setTimeout(() => void this.search(), DEBOUNCE_MS);
  }

  onEnter(): void {
    const book = this.bookShorthand();
    if (book) {
      void this.bookShorthandNow(book);
      return;
    }
    if (this.debounceHandle !== null) {
      clearTimeout(this.debounceHandle);
      this.debounceHandle = null;
    }
    void this.search();
  }

  /** Enter on "45m ST6-124 Kommentar": book today, task-redirect policy included. */
  private async bookShorthandNow(book: BookShorthand): Promise<void> {
    if (this.booking()) {
      return;
    }
    this.booking.set(true);
    this.error.set(null);
    const minutes = this.settings.round(book.minutes);
    try {
      const outcome = await this.bookingService.bookWithPolicy({
        issueId: book.issueId,
        date: toIsoDate(new Date()),
        minutes,
        typeId: this.bookingService.lastTypeId,
        text: book.comment || null,
      });
      if (outcome.status === 'booked') {
        const redirect = outcome.redirectedFrom ? ` (↪ ${outcome.redirectedFrom})` : '';
        this.toast.show(`✓ ${formatDuration(minutes)} auf ${outcome.item.issueId} gebucht${redirect}`);
        this.close();
      } else {
        // Ambiguous/noTask — hand over to the nested LogTimeDialog (its pre-flight
        // shows the subtask select / feature confirmation).
        this.prefillMinutes.set(minutes);
        this.logIssue.set({ issueId: book.issueId, summary: '' } as TaskListItem);
      }
    } catch (err) {
      this.error.set((err as Error).message);
    } finally {
      this.booking.set(false);
    }
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
    this.prefillMinutes.set(null);
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
