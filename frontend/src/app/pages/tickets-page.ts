import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { InlineBook } from '../components/inline-book';
import { LogTimeDialog } from '../dialogs/log-time-dialog';
import { parseDuration, relativeTime } from '../format';
import { TaskListItem } from '../models';
import { ApiService } from '../services/api.service';
import { BookingService } from '../services/booking.service';
import { DevService } from '../services/dev.service';
import { RefreshService } from '../services/refresh.service';
import { TimerService } from '../services/timer.service';

type Segment = 'mine' | 'pool' | 'sprint';
type SortKey = 'id' | 'state' | 'developer' | 'priority' | 'spent' | 'updated';

interface StateCount {
  state: string;
  count: number;
}

/** Full ticket table: own tickets + sprint pool, status chips, sortable columns, inline booking. */
@Component({
  selector: 'app-tickets-page',
  imports: [FormsModule, LogTimeDialog, InlineBook],
  template: `
    <div class="page">
      <div class="toolbar">
        <span class="segmented" role="tablist">
          <button
            type="button"
            role="tab"
            [class.active]="segment() === 'mine'"
            (click)="segment.set('mine')"
          >
            Meine Tickets
          </button>
          <button
            type="button"
            role="tab"
            [class.active]="segment() === 'pool'"
            (click)="segment.set('pool')"
          >
            Sprint-Pool
          </button>
          <button
            type="button"
            role="tab"
            [class.active]="segment() === 'sprint'"
            (click)="segment.set('sprint')"
            title="Alle Tickets im aktuellen Sprint — z.B. für Testing/Review auf Kollegen-Tickets"
          >
            Sprint (alle)
          </button>
        </span>
        <input
          type="search"
          class="filter-input"
          placeholder="Filtern nach ID, Titel, Status…"
          [(ngModel)]="filter"
          aria-label="Tickets filtern"
        />
        <button type="button" (click)="load(true)" [disabled]="loading()">Aktualisieren</button>
        <span class="muted">{{ filtered().length }} von {{ current().length }}</span>
      </div>

      @if (stateCounts().length > 1) {
        <div class="state-chips">
          @for (s of stateCounts(); track s.state) {
            <button
              type="button"
              class="state-chip"
              [class.active]="stateFilter() === s.state"
              (click)="toggleState(s.state)"
            >
              {{ s.state }} <span class="muted">{{ s.count }}</span>
            </button>
          }
        </div>
      }

      @if (error(); as err) {
        <div class="banner error">
          {{ err }}
          <button type="button" class="link" (click)="error.set(null)">schliessen</button>
        </div>
      }

      @if (loading()) {
        <div class="loading"><span class="spinner"></span> Tickets laden…</div>
      } @else {
        <div class="table-scroll sticky-head">
          <table class="data-table">
            <thead>
              <tr>
                <th class="sortable" (click)="sortBy('id')">ID{{ arrow('id') }}</th>
                <th>Titel</th>
                <th class="sortable" (click)="sortBy('state')">Status{{ arrow('state') }}</th>
                @if (segment() === 'sprint') {
                  <th class="sortable" (click)="sortBy('developer')">Entwickler{{ arrow('developer') }}</th>
                }
                <th class="sortable" (click)="sortBy('priority')">Priorität{{ arrow('priority') }}</th>
                <th class="sortable nowrap" (click)="sortBy('spent')" title="Gebucht / Schätzung">
                  Ist / Soll{{ arrow('spent') }}
                </th>
                <th class="sortable" (click)="sortBy('updated')">Aktualisiert{{ arrow('updated') }}</th>
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
                  <td class="nowrap"><span class="badge neutral">{{ t.state ?? '–' }}</span></td>
                  @if (segment() === 'sprint') {
                    <td class="nowrap" [title]="developerName(t)">{{ t.developer ?? '–' }}</td>
                  }
                  <td class="nowrap">{{ t.priority ?? '–' }}</td>
                  <td class="nowrap" [class.over]="isOver(t)">
                    {{ t.spent ?? '0m' }} <span class="muted">/ {{ t.estimate ?? '–' }}</span>
                  </td>
                  <td class="nowrap muted">{{ rel(t.updated) }}</td>
                  @if (dev.isSelf()) {
                    <td class="nowrap inline-book-cell">
                      <app-inline-book [issue]="t" />
                    </td>
                  }
                  <td class="nowrap row-actions">
                    @if (dev.isSelf()) {
                      <button type="button" class="icon" title="Timer starten" (click)="start(t)">▶</button>
                      <button type="button" class="icon" title="Zeit buchen" (click)="logIssue.set(t)">✎</button>
                    } @else {
                      <span class="muted" title="Buchungen werden immer als du erstellt — wechsle zurück zu dir selbst">nur lesen</span>
                    }
                  </td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="11" class="muted empty-cell">
                    @if (current().length === 0) {
                      @if (segment() === 'pool') {
                        Kein Sprint-Pool (oder keine Pool-Query konfiguriert).
                      } @else if (segment() === 'sprint') {
                        Keine Sprint-Tickets — youTrack.sprintQuery in config.json konfigurieren
                        (z.B. Board ST6-Sprint: {{ '{' }}Aktueller Sprint{{ '}' }}).
                      } @else {
                        Keine Tickets gefunden.
                      }
                    } @else {
                      Keine Tickets passen zum Filter.
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
export class TicketsPage {
  private readonly api = inject(ApiService);
  private readonly timer = inject(TimerService);
  private readonly refresh = inject(RefreshService);
  protected readonly dev = inject(DevService);
  protected readonly booking = inject(BookingService);

  readonly issues = signal<TaskListItem[]>([]);
  readonly pool = signal<TaskListItem[]>([]);
  readonly sprint = signal<TaskListItem[]>([]);
  readonly segment = signal<Segment>('mine');
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly filter = signal('');
  readonly stateFilter = signal<string | null>(null);
  readonly sortKey = signal<SortKey | null>(null);
  readonly sortAsc = signal(true);
  readonly logIssue = signal<TaskListItem | null>(null);

  readonly current = computed(() => {
    switch (this.segment()) {
      case 'mine':
        return this.issues();
      case 'pool':
        return this.pool();
      case 'sprint':
        return this.sprint();
    }
  });

  readonly stateCounts = computed<StateCount[]>(() => {
    const counts = new Map<string, number>();
    for (const issue of this.current()) {
      const state = issue.state ?? 'Ohne Status';
      counts.set(state, (counts.get(state) ?? 0) + 1);
    }
    return [...counts.entries()]
      .map(([state, count]) => ({ state, count }))
      .sort((a, b) => b.count - a.count);
  });

  readonly filtered = computed(() => {
    const query = this.filter().trim().toLowerCase();
    const state = this.stateFilter();
    let rows = this.current().filter(
      (t) =>
        (!state || (t.state ?? 'Ohne Status') === state) &&
        (!query ||
          t.issueId.toLowerCase().includes(query) ||
          t.summary.toLowerCase().includes(query) ||
          (t.state ?? '').toLowerCase().includes(query) ||
          (t.developer ?? '').toLowerCase().includes(query)),
    );
    const key = this.sortKey();
    if (key) {
      const direction = this.sortAsc() ? 1 : -1;
      rows = [...rows].sort((a, b) => direction * this.compare(a, b, key));
    }
    return rows;
  });

  constructor() {
    effect(() => {
      this.dev.devParam();
      this.refresh.worklogVersion();
      untracked(() => void this.load(false));
    });
    // Switching segments resets the state filter (counts differ per segment).
    effect(() => {
      this.segment();
      untracked(() => this.stateFilter.set(null));
    });
  }

  async load(refresh: boolean): Promise<void> {
    // Spinner only on first load — background reloads (after a booking) must not
    // tear down the table and its inline-book success chips.
    if (this.current().length === 0) {
      this.loading.set(true);
    }
    this.error.set(null);
    try {
      const [issues, pool, sprint] = await Promise.all([
        this.api.getIssues(refresh, this.dev.devParam()),
        this.api.getSprintPool(refresh, this.dev.devParam()).catch(() => [] as TaskListItem[]),
        this.api.getSprintIssues(refresh).catch(() => [] as TaskListItem[]),
      ]);
      this.issues.set(issues);
      this.pool.set(pool);
      this.sprint.set(sprint);
      this.booking.prefetch(issues);
      this.booking.prefetch(sprint);
    } catch (err) {
      this.error.set((err as Error).message);
    } finally {
      this.loading.set(false);
    }
  }

  toggleState(state: string): void {
    this.stateFilter.update((current) => (current === state ? null : state));
  }

  sortBy(key: SortKey): void {
    if (this.sortKey() === key) {
      if (this.sortAsc()) {
        this.sortAsc.set(false);
      } else {
        this.sortKey.set(null); // third click restores server order
      }
    } else {
      this.sortKey.set(key);
      this.sortAsc.set(true);
    }
  }

  arrow(key: SortKey): string {
    return this.sortKey() === key ? (this.sortAsc() ? ' ▲' : ' ▼') : '';
  }

  private compare(a: TaskListItem, b: TaskListItem, key: SortKey): number {
    switch (key) {
      case 'id':
        return a.issueId.localeCompare(b.issueId, undefined, { numeric: true });
      case 'state':
        return (a.state ?? '').localeCompare(b.state ?? '');
      case 'developer':
        return (a.developer ?? '').localeCompare(b.developer ?? '');
      case 'priority':
        return (a.priority ?? '').localeCompare(b.priority ?? '');
      case 'spent':
        return (parseDuration(a.spent ?? '') ?? 0) - (parseDuration(b.spent ?? '') ?? 0);
      case 'updated':
        return a.updated.localeCompare(b.updated);
    }
  }

  isOver(t: TaskListItem): boolean {
    const spent = parseDuration(t.spent ?? '');
    const estimate = parseDuration(t.estimate ?? '');
    return spent !== null && estimate !== null && spent > estimate;
  }

  developerName(item: TaskListItem): string {
    if (!item.developer) {
      return 'Kein Entwickler zugewiesen';
    }
    const user = this.dev
      .users()
      .find((u) => u.login.toLowerCase() === item.developer!.toLowerCase());
    return user?.fullName ?? item.developer;
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
