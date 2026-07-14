import { Injectable, computed, inject, signal } from '@angular/core';
import { UserSettings } from '../models';
import { ApiService } from './api.service';

const AI_KEY = 'you-tracker.aiOn';
const TIMER_KEY = 'you-tracker.timerOn';

const DEFAULTS: UserSettings = {
  usePresence: true,
  targetMinutes: null,
  defaultIssueId: null,
  defaultIssueSummary: null,
  defaultTypeId: null,
  defaultTypeName: null,
  roundingMinutes: 0,
};

/**
 * User settings from the settings dialog. The functional part (presence semantics,
 * fixed target, default ticket/type, rounding) is server-persisted (settings.json);
 * pure UI toggles (assistant card, timer widget) live in localStorage.
 */
@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly api = inject(ApiService);

  readonly settings = signal<UserSettings>(DEFAULTS);
  readonly loaded = signal(false);

  /** Fixed daily target from /api/meta (config workday.targetHours), in minutes. */
  readonly metaTargetMinutes = signal(480);

  readonly usePresence = computed(() => this.settings().usePresence);
  readonly roundingMinutes = computed(() => this.settings().roundingMinutes);
  readonly defaultIssueId = computed(() => this.settings().defaultIssueId);
  readonly defaultIssueSummary = computed(() => this.settings().defaultIssueSummary);
  readonly defaultTypeId = computed(() => this.settings().defaultTypeId);

  /** Effective fixed daily target: settings override or the config's workday target. */
  readonly fixedTargetMinutes = computed(
    () => this.settings().targetMinutes ?? this.metaTargetMinutes(),
  );

  // UI-only prefs (localStorage)
  readonly aiOn = signal(readBool(AI_KEY, true));
  readonly timerOn = signal(readBool(TIMER_KEY, true));

  constructor() {
    void this.api
      .getSettings()
      .then((s) => {
        this.settings.set(s);
        this.loaded.set(true);
      })
      .catch(() => undefined);
    void this.api
      .getMeta()
      .then((meta) => this.metaTargetMinutes.set(meta.targetMinutesPerWorkday))
      .catch(() => undefined);
  }

  async save(changes: Partial<UserSettings>): Promise<void> {
    const next = { ...this.settings(), ...changes };
    this.settings.set(next); // optimistic — the dialog edits should feel instant
    try {
      this.settings.set(await this.api.saveSettings(next));
    } catch (err) {
      this.settings.set(await this.api.getSettings().catch(() => next));
      throw err;
    }
  }

  setAiOn(value: boolean): void {
    this.aiOn.set(value);
    writeBool(AI_KEY, value);
  }

  setTimerOn(value: boolean): void {
    this.timerOn.set(value);
    writeBool(TIMER_KEY, value);
  }

  /** Applies the configured rounding to a duration (timer stops, quick bookings). */
  round(minutes: number): number {
    const r = this.roundingMinutes();
    if (r <= 0) {
      return minutes;
    }
    return Math.max(r, Math.round(minutes / r) * r);
  }
}

function readBool(key: string, fallback: boolean): boolean {
  try {
    const raw = localStorage.getItem(key);
    return raw === null ? fallback : raw === 'true';
  } catch {
    return fallback;
  }
}

function writeBool(key: string, value: boolean): void {
  try {
    localStorage.setItem(key, String(value));
  } catch {
    // storage unavailable — pref just won't persist
  }
}
