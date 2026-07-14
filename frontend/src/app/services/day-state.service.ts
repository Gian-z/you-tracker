import { Injectable, computed, inject, signal } from '@angular/core';
import { addDays, startOfWeek, toIsoDate } from '../format';
import { DayState } from '../models';
import { ApiService } from './api.service';

export const EMPTY_DAY_STATE: DayState = { come: null, go: null, pauseMinutes: 0, absence: 'none' };

/**
 * Per-day presence stamps (Komme/Gehe/Pause) and personal absences, server-persisted
 * in day-state.json. Loads the current week on start; `nowMinutes` ticks every 30s so
 * the live presence ("Gehe" still open) keeps counting without a reload.
 */
@Injectable({ providedIn: 'root' })
export class DayStateService {
  private readonly api = inject(ApiService);

  readonly states = signal<ReadonlyMap<string, DayState>>(new Map());
  readonly nowMinutes = signal(minutesOfDay());

  readonly todayIso = signal(toIsoDate(new Date()));
  readonly today = computed(() => this.states().get(this.todayIso()) ?? EMPTY_DAY_STATE);

  private readonly saveTimers = new Map<string, ReturnType<typeof setTimeout>>();

  constructor() {
    void this.loadCurrentWeek();
    setInterval(() => {
      this.nowMinutes.set(minutesOfDay());
      this.todayIso.set(toIsoDate(new Date()));
    }, 30_000);
  }

  stateFor(date: string): DayState {
    return this.states().get(date) ?? EMPTY_DAY_STATE;
  }

  async loadRange(from: string, to: string): Promise<void> {
    try {
      const loaded = await this.api.getDayStates(from, to);
      this.states.update((map) => {
        const next = new Map(map);
        for (const [date, state] of Object.entries(loaded)) {
          next.set(date, state);
        }
        return next;
      });
    } catch {
      // presence simply stays empty — the page still works with the fixed target
    }
  }

  /** Optimistic update + debounced PUT (typing in the presence inputs must not spam the API). */
  update(date: string, changes: Partial<DayState>): void {
    const next = { ...this.stateFor(date), ...changes };
    this.states.update((map) => new Map(map).set(date, next));
    const existing = this.saveTimers.get(date);
    if (existing) {
      clearTimeout(existing);
    }
    this.saveTimers.set(
      date,
      setTimeout(() => {
        this.saveTimers.delete(date);
        void this.api.saveDayState(date, this.stateFor(date)).catch(() => undefined);
      }, 600),
    );
  }

  private loadCurrentWeek(): Promise<void> {
    const monday = startOfWeek(new Date());
    return this.loadRange(toIsoDate(monday), toIsoDate(addDays(monday, 6)));
  }
}

function minutesOfDay(): number {
  const now = new Date();
  return now.getHours() * 60 + now.getMinutes();
}
