import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { toIsoDate } from '../format';
import { ApiService } from './api.service';
import { DevService } from './dev.service';
import { RefreshService } from './refresh.service';

/**
 * Today's booked-vs-target minutes for the permanent topbar chip — the
 * "habe ich heute alles gebucht?" answer visible from every page.
 */
@Injectable({ providedIn: 'root' })
export class TodayStatusService {
  private readonly api = inject(ApiService);
  private readonly refresh = inject(RefreshService);
  private readonly dev = inject(DevService);

  readonly bookedMinutes = signal(0);
  readonly targetMinutes = signal(0);
  readonly loaded = signal(false);

  readonly gapMinutes = computed(() => Math.max(0, this.targetMinutes() - this.bookedMinutes()));
  readonly reached = computed(() => this.loaded() && this.gapMinutes() === 0);

  constructor() {
    effect(() => {
      this.refresh.worklogVersion();
      this.dev.devParam();
      untracked(() => void this.load());
    });
  }

  private async load(): Promise<void> {
    const today = toIsoDate(new Date());
    try {
      const overview = await this.api.getOverview(today, today, false, this.dev.devParam());
      const day = overview.days.find((d) => d.date === today);
      this.bookedMinutes.set(day?.bookedMinutes ?? overview.totalBookedMinutes);
      this.targetMinutes.set(day?.targetMinutes ?? overview.totalTargetMinutes);
      this.loaded.set(true);
    } catch {
      this.loaded.set(false); // chip simply stays hidden
    }
  }
}
