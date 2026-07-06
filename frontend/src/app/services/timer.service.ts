import { Injectable, computed, inject, signal } from '@angular/core';
import { TimerState, TimerStopResult } from '../models';
import { ApiService } from './api.service';

@Injectable({ providedIn: 'root' })
export class TimerService {
  private readonly api = inject(ApiService);
  private readonly now = signal(Date.now());

  readonly state = signal<TimerState | null>(null);

  readonly elapsedSeconds = computed(() => {
    const state = this.state();
    if (!state) {
      return 0;
    }
    const started = parseUtc(state.startedUtc);
    if (Number.isNaN(started)) {
      return 0;
    }
    return Math.max(0, Math.floor((this.now() - started) / 1000));
  });

  constructor() {
    setInterval(() => this.now.set(Date.now()), 1000);
    void this.refresh();
  }

  async refresh(): Promise<void> {
    try {
      this.state.set(await this.api.getTimer());
    } catch {
      // backend unreachable at startup — leave state as-is
    }
  }

  async start(issueId: string, issueSummary: string): Promise<void> {
    const state = await this.api.startTimer(issueId, issueSummary);
    this.state.set(state);
  }

  async stop(): Promise<TimerStopResult | null> {
    const result = await this.api.stopTimer();
    this.state.set(null);
    return result;
  }
}

/** Parses an ISO datetime, assuming UTC when no timezone designator is present. */
function parseUtc(iso: string): number {
  const hasZone = /(?:z|[+-]\d{2}:?\d{2})$/i.test(iso);
  return Date.parse(hasZone ? iso : `${iso}Z`);
}
