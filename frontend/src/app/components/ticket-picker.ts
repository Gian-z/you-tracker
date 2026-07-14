import { Component, ElementRef, inject, input, output, signal } from '@angular/core';
import { TaskListItem } from '../models';
import { ApiService } from '../services/api.service';

/**
 * Mockup's "ST6-124 ▾" ticket chooser: a chip button opening an anchored dropdown with a
 * global YouTrack search (debounced). Emits the picked issue; the host owns the value.
 */
@Component({
  selector: 'app-ticket-picker',
  host: { '(document:click)': 'onDocumentClick($event)' },
  template: `
    <span class="ticket-picker">
      <button
        type="button"
        class="ticket-picker-chip num"
        [class.placeholder]="!issueId()"
        (click)="toggle()"
        [title]="issueSummary() || 'Ticket wählen'"
      >
        {{ issueId() || placeholder() }} ▾
      </button>
      @if (open()) {
        <span class="ticket-picker-pop">
          <input
            #searchBox
            type="text"
            class="ticket-picker-search"
            [value]="query()"
            (input)="onQuery($event)"
            placeholder="Ticket suchen…"
            aria-label="Ticket suchen"
          />
          @if (searching()) {
            <span class="ticket-picker-note"><span class="spinner"></span> Suche…</span>
          } @else if (error(); as err) {
            <span class="ticket-picker-note danger">{{ err }}</span>
          } @else if (results().length === 0 && query().length >= 2) {
            <span class="ticket-picker-note">Keine Treffer</span>
          }
          @for (r of results(); track r.issueId) {
            <button type="button" class="ticket-picker-item" (click)="pick(r)">
              <span class="num item-id">{{ r.issueId }}</span>
              <span class="item-summary">{{ r.summary }}</span>
              @if (r.state) {
                <span class="item-state">{{ r.state }}</span>
              }
            </button>
          }
        </span>
      }
    </span>
  `,
})
export class TicketPicker {
  private readonly api = inject(ApiService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly issueId = input<string | null>(null);
  readonly issueSummary = input<string | null>(null);
  readonly placeholder = input('Ticket wählen');

  readonly picked = output<TaskListItem>();

  protected readonly open = signal(false);
  protected readonly query = signal('');
  protected readonly results = signal<TaskListItem[]>([]);
  protected readonly searching = signal(false);
  protected readonly error = signal<string | null>(null);

  private debounce: ReturnType<typeof setTimeout> | null = null;
  private searchSeq = 0;

  protected toggle(): void {
    this.open.set(!this.open());
    if (this.open()) {
      this.query.set('');
      this.results.set([]);
      this.error.set(null);
      setTimeout(() => {
        this.host.nativeElement.querySelector<HTMLInputElement>('.ticket-picker-search')?.focus();
      });
    }
  }

  protected onQuery(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.query.set(value);
    if (this.debounce) {
      clearTimeout(this.debounce);
    }
    if (value.trim().length < 2) {
      this.results.set([]);
      return;
    }
    this.debounce = setTimeout(() => void this.search(value.trim()), 300);
  }

  protected pick(item: TaskListItem): void {
    this.open.set(false);
    this.picked.emit(item);
  }

  protected onDocumentClick(event: Event): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) {
      this.open.set(false);
    }
  }

  private async search(q: string): Promise<void> {
    const seq = ++this.searchSeq;
    this.searching.set(true);
    this.error.set(null);
    try {
      const items = await this.api.searchIssues(q, 8);
      if (seq === this.searchSeq) {
        this.results.set(items);
      }
    } catch (err) {
      if (seq === this.searchSeq) {
        this.error.set((err as Error).message);
        this.results.set([]);
      }
    } finally {
      if (seq === this.searchSeq) {
        this.searching.set(false);
      }
    }
  }
}
