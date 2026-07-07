import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { IssueSearchDialog } from './dialogs/issue-search-dialog';
import { LogTimeDialog } from './dialogs/log-time-dialog';
import { formatClock, formatElapsed } from './format';
import { TimerStopResult } from './models';
import { DevService } from './services/dev.service';
import { ThemeService } from './services/theme.service';
import { TimerService } from './services/timer.service';
import { TodayStatusService } from './services/today-status.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, LogTimeDialog, IssueSearchDialog],
  host: { '(document:keydown.control.k)': 'openSearch($event)' },
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly timer = inject(TimerService);
  protected readonly dev = inject(DevService);
  protected readonly theme = inject(ThemeService);
  protected readonly todayStatus = inject(TodayStatusService);
  private readonly router = inject(Router);

  constructor() {
    void this.dev.init();
  }

  private readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map((e) => e.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  /** Sprint and Tickets carry wide tables — give them more room. */
  protected readonly wide = computed(() => {
    const url = this.currentUrl();
    return url.startsWith('/sprint') || url.startsWith('/tickets');
  });

  protected readonly todayChip = computed(() => {
    const s = this.todayStatus;
    return `Heute ${formatClock(s.bookedMinutes())} / ${formatClock(s.targetMinutes())}`;
  });

  protected openSearch(event?: Event): void {
    event?.preventDefault(); // beat the browser's own Ctrl+K binding
    this.searchOpen.set(true);
  }

  protected onDevChange(event: Event): void {
    this.dev.select((event.target as HTMLSelectElement | HTMLInputElement).value);
  }

  protected readonly elapsed = computed(() => formatElapsed(this.timer.elapsedSeconds()));
  protected readonly searchOpen = signal(false);
  protected readonly stopping = signal(false);
  protected readonly stopResult = signal<TimerStopResult | null>(null);
  protected readonly timerError = signal<string | null>(null);

  protected async stopTimer(): Promise<void> {
    if (this.stopping()) {
      return;
    }
    this.stopping.set(true);
    this.timerError.set(null);
    try {
      const result = await this.timer.stop();
      if (result) {
        this.stopResult.set(result);
      }
    } catch (err) {
      this.timerError.set((err as Error).message);
    } finally {
      this.stopping.set(false);
    }
  }

  /** Booking confirmed — only now is the persisted timer cleared. Cancel keeps it running. */
  protected async onStopLogSaved(): Promise<void> {
    try {
      await this.timer.discard();
    } catch (err) {
      this.timerError.set((err as Error).message);
    }
  }
}
