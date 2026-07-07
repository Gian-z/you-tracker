import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { LogTimeDialog } from './dialogs/log-time-dialog';
import { formatElapsed } from './format';
import { TimerStopResult } from './models';
import { DevService } from './services/dev.service';
import { TimerService } from './services/timer.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, LogTimeDialog],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly timer = inject(TimerService);
  protected readonly dev = inject(DevService);

  constructor() {
    void this.dev.init();
  }

  protected onDevChange(event: Event): void {
    this.dev.select((event.target as HTMLSelectElement | HTMLInputElement).value);
  }

  protected readonly elapsed = computed(() => formatElapsed(this.timer.elapsedSeconds()));
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
