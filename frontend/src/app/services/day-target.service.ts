import { Injectable, computed, inject } from '@angular/core';
import { parseClock } from '../format';
import { DayState } from '../models';
import { DayStateService } from './day-state.service';
import { SettingsService } from './settings.service';

/**
 * Day-target semantics from the design mockup, in one place:
 *
 *   presence  = (go ?? max(now, come)) − come − pause      (live while "Gehe" is open)
 *   soll      = fixedTarget × absence factor (1 / 0.5 / 0)
 *   target    = usePresence ? max(60, presence) : soll     ("book everything you were present")
 *   saldo     = presence − soll                            (overtime balance)
 *
 * Non-today days always measure against soll (presence stamps only exist live).
 */
@Injectable({ providedIn: 'root' })
export class DayTargetService {
  private readonly settings = inject(SettingsService);
  private readonly dayState = inject(DayStateService);

  readonly presenceMinutes = computed(() => {
    const state = this.dayState.today();
    const come = parseClock(state.come ?? '');
    if (come === null) {
      return null; // not stamped yet
    }
    const go = state.go ? parseClock(state.go) : null;
    const end = go ?? Math.max(this.dayState.nowMinutes(), come);
    return Math.max(0, end - come - state.pauseMinutes);
  });

  /** True while "Gehe" is not stamped — presence is counting live. */
  readonly presenceRunning = computed(() => {
    const state = this.dayState.today();
    return parseClock(state.come ?? '') !== null && !state.go;
  });

  readonly sollToday = computed(() =>
    this.sollFor(this.dayState.today(), this.settings.fixedTargetMinutes()),
  );

  readonly targetToday = computed(() => {
    const presence = this.presenceMinutes();
    if (this.settings.usePresence() && presence !== null) {
      return Math.max(60, presence);
    }
    return this.sollToday();
  });

  readonly saldoMinutes = computed(() => {
    const presence = this.presenceMinutes();
    return presence === null ? null : presence - this.sollToday();
  });

  /** Whether today's target comes from presence ("Präsenz") or the fixed target ("Soll"). */
  readonly targetSource = computed(() =>
    this.settings.usePresence() && this.presenceMinutes() !== null ? 'Präsenz' : 'Soll',
  );

  /** Target for an arbitrary day: today → live target, otherwise soll with the day's absence. */
  targetFor(date: string, isToday: boolean): number {
    if (isToday) {
      return this.targetToday();
    }
    return this.sollFor(this.dayState.stateFor(date), this.settings.fixedTargetMinutes());
  }

  private sollFor(state: DayState, fixedTarget: number): number {
    const factor = state.absence === 'full' ? 0 : state.absence === 'half' ? 0.5 : 1;
    return Math.round(fixedTarget * factor);
  }
}
