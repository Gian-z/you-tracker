import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { IssueSearchDialog } from './dialogs/issue-search-dialog';
import { LogTimeDialog } from './dialogs/log-time-dialog';
import { SettingsDialog } from './dialogs/settings-dialog';
import { formatClock, formatElapsed } from './format';
import { TimerStopResult } from './models';
import { DayTargetService } from './services/day-target.service';
import { DevService } from './services/dev.service';
import { SearchService } from './services/search.service';
import { SettingsService } from './services/settings.service';
import { SettingsUiService } from './services/settings-ui.service';
import { ThemeService } from './services/theme.service';
import { ToastService } from './services/toast.service';
import { TimerService } from './services/timer.service';
import { TodayStatusService } from './services/today-status.service';

@Component({
  selector: 'app-root',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    LogTimeDialog,
    IssueSearchDialog,
    SettingsDialog,
  ],
  host: { '(document:keydown.control.k)': 'openSearch($event)' },
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly timer = inject(TimerService);
  protected readonly dev = inject(DevService);
  protected readonly theme = inject(ThemeService);
  protected readonly todayStatus = inject(TodayStatusService);
  protected readonly dayTarget = inject(DayTargetService);
  protected readonly search = inject(SearchService);
  protected readonly toast = inject(ToastService);
  protected readonly settings = inject(SettingsService);
  protected readonly settingsUi = inject(SettingsUiService);
  private readonly router = inject(Router);

  protected readonly devMenuOpen = signal(false);

  constructor() {
    void this.dev.init();
  }

  protected selectDev(login: string): void {
    this.dev.select(login);
    this.devMenuOpen.set(false);
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
    return `${formatClock(s.bookedMinutes())} / ${formatClock(s.targetMinutes())}`;
  });

  protected openSearch(event?: Event): void {
    event?.preventDefault(); // beat the browser's own Ctrl+K binding
    this.search.open.set(true);
  }

  protected onDevChange(event: Event): void {
    this.dev.select((event.target as HTMLSelectElement | HTMLInputElement).value);
  }

  protected readonly elapsed = computed(() => formatElapsed(this.timer.elapsedSeconds()));
  protected readonly busy = signal(false);
  protected readonly stopResult = signal<TimerStopResult | null>(null);
  protected readonly timerError = signal<string | null>(null);
  protected readonly confirmDiscard = signal(false);
  private discardResetHandle: ReturnType<typeof setTimeout> | null = null;

  protected readonly discardLabel = computed(
    () => `${Math.max(1, Math.round(this.timer.elapsedSeconds() / 60))}m`,
  );

  /** Stop = open the prefilled booking dialog; the timer store stays until saved/discarded. */
  protected async stopTimer(): Promise<void> {
    await this.timerAction(async () => {
      const result = await this.timer.stop();
      if (result) {
        // "Rundung: Timer- & Schnellbuchungen" — the prefill is rounded, still editable.
        this.stopResult.set({ ...result, elapsedMinutes: this.settings.round(result.elapsedMinutes) });
      }
    });
  }

  protected async togglePause(): Promise<void> {
    await this.timerAction(() =>
      this.timer.isPaused() ? this.timer.resume() : this.timer.pause(),
    );
  }

  /** First click arms the red "27m verwerfen?" state; a second within 4s discards. */
  protected armDiscard(): void {
    this.confirmDiscard.set(true);
    this.clearDiscardReset();
    this.discardResetHandle = setTimeout(() => this.confirmDiscard.set(false), 4000);
  }

  protected async discardTimer(): Promise<void> {
    await this.timerAction(() => this.timer.discard());
  }

  /** Booking confirmed — only now is the persisted timer cleared. Cancel keeps it running. */
  protected async onStopLogSaved(): Promise<void> {
    try {
      await this.timer.discard();
    } catch (err) {
      this.timerError.set((err as Error).message);
    }
  }

  /** Shared in-flight guard; any timer action disarms a pending discard confirm. */
  private async timerAction(work: () => Promise<void>): Promise<void> {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.timerError.set(null);
    this.confirmDiscard.set(false);
    this.clearDiscardReset();
    try {
      await work();
    } catch (err) {
      this.timerError.set((err as Error).message);
    } finally {
      this.busy.set(false);
    }
  }

  private clearDiscardReset(): void {
    if (this.discardResetHandle !== null) {
      clearTimeout(this.discardResetHandle);
      this.discardResetHandle = null;
    }
  }
}
