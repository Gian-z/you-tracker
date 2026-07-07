import { Injectable, effect, inject, signal } from '@angular/core';
import { BookingTarget, TaskListItem, WorkItem, WorkLogRequest } from '../models';
import { ApiService } from './api.service';
import { RefreshService } from './refresh.service';

const LAST_TYPE_KEY = 'you-tracker.lastTypeId';

export type BookOutcome =
  | { status: 'booked'; item: WorkItem; redirectedFrom: string | null }
  | { status: 'needs-picker'; target: BookingTarget };

/**
 * Central booking policy: every write goes through here so the "bookings land on
 * tasks" rule (pre-flight resolve + redirect/picker) and the worklog-changed
 * refresh behave identically for dialogs, presets and inline booking.
 */
@Injectable({ providedIn: 'root' })
export class BookingService {
  private readonly api = inject(ApiService);
  private readonly refresh = inject(RefreshService);

  /** Issue types the backend treats as Features (from /api/meta). */
  readonly featureTypes = signal<string[]>(['Feature']);

  /** Resolved booking targets by issue id — feeds the ↪ badges in ticket tables. */
  readonly resolutions = signal<ReadonlyMap<string, BookingTarget>>(new Map());

  private readonly pending = new Map<string, Promise<BookingTarget>>();

  constructor() {
    void this.api
      .getMeta()
      .then((meta) => this.featureTypes.set(meta.featureTypes ?? ['Feature']))
      .catch(() => undefined);
    // Subtask structure can change after a booking (backend cache is evicted too).
    effect(() => {
      this.refresh.worklogVersion();
      this.pending.clear();
      this.resolutions.set(new Map());
    });
  }

  isFeature(item: TaskListItem): boolean {
    return (
      item.type !== null &&
      this.featureTypes().some((t) => t.toLowerCase() === item.type!.toLowerCase())
    );
  }

  resolve(issueId: string): Promise<BookingTarget> {
    const cached = this.resolutions().get(issueId);
    if (cached) {
      return Promise.resolve(cached);
    }
    let inflight = this.pending.get(issueId);
    if (!inflight) {
      inflight = this.api.getBookingTarget(issueId).then((target) => {
        this.resolutions.update((map) => new Map(map).set(issueId, target));
        return target;
      });
      this.pending.set(issueId, inflight);
    }
    return inflight;
  }

  /** Fire-and-forget resolution for Feature rows so table badges have tooltips. */
  prefetch(items: TaskListItem[]): void {
    for (const item of items) {
      if (this.isFeature(item)) {
        void this.resolve(item.issueId).catch(() => undefined);
      }
    }
  }

  resolutionFor(issueId: string): BookingTarget | undefined {
    return this.resolutions().get(issueId);
  }

  /**
   * Book with the task-redirect policy applied. Returns 'needs-picker' when the
   * user must choose (ambiguous subtasks / feature without task) — continue via
   * book() with the picked issue id.
   */
  async bookWithPolicy(request: WorkLogRequest): Promise<BookOutcome> {
    const target = await this.resolve(request.issueId).catch(() => null);
    if (target && (target.kind === 'ambiguous' || target.kind === 'noTask')) {
      return { status: 'needs-picker', target };
    }
    const issueId = target?.kind === 'redirected' ? target.targetIssueId : request.issueId;
    const item = await this.book({ ...request, issueId });
    return {
      status: 'booked',
      item,
      redirectedFrom: issueId === request.issueId ? null : request.issueId,
    };
  }

  /** Direct write (target already decided) + app-wide refresh. */
  async book(request: WorkLogRequest): Promise<WorkItem> {
    const item = await this.api.createWorklog(request);
    if (request.typeId) {
      this.lastTypeId = request.typeId;
    }
    this.refresh.worklogChanged();
    return item;
  }

  /** Edit an existing booking — no target redirect (the item already sits on its issue). */
  async update(
    item: WorkItem,
    changes: { date: string; minutes: number; typeId?: string | null; text?: string | null },
  ): Promise<WorkItem> {
    const updated = await this.api.updateWorklog(item.issueId, item.id, changes);
    this.refresh.worklogChanged();
    return updated;
  }

  async remove(item: WorkItem): Promise<void> {
    await this.api.deleteWorklog(item.issueId, item.id);
    this.refresh.worklogChanged();
  }

  /** Last used work type — default for inline booking and the log dialog. */
  get lastTypeId(): string | null {
    try {
      return localStorage.getItem(LAST_TYPE_KEY);
    } catch {
      return null;
    }
  }

  set lastTypeId(value: string | null) {
    try {
      if (value) {
        localStorage.setItem(LAST_TYPE_KEY, value);
      }
    } catch {
      // storage unavailable — default just won't stick
    }
  }
}
