import { Injectable, effect, signal } from '@angular/core';

export type ThemePreference = 'light' | 'dark' | 'system';

const STORAGE_KEY = 'you-tracker.theme';

/**
 * Theme preference: system by default, manually overridable via the topbar toggle.
 * A ?theme=light|dark query param forces the theme for the session without persisting
 * it (used by headless screenshot verification).
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly preference = signal<ThemePreference>(readInitialPreference());

  private readonly fromQueryParam = readQueryParam() !== null;

  constructor() {
    effect(() => {
      const pref = this.preference();
      const root = document.documentElement;
      if (pref === 'system') {
        delete root.dataset['theme'];
      } else {
        root.dataset['theme'] = pref;
      }
      if (!this.fromQueryParam) {
        try {
          if (pref === 'system') {
            localStorage.removeItem(STORAGE_KEY);
          } else {
            localStorage.setItem(STORAGE_KEY, pref);
          }
        } catch {
          // storage unavailable — theme just won't persist
        }
      }
    });
  }

  /** True when the effective (resolved) theme is dark. */
  isDark(): boolean {
    const pref = this.preference();
    if (pref !== 'system') {
      return pref === 'dark';
    }
    return window.matchMedia('(prefers-color-scheme: dark)').matches;
  }

  toggle(): void {
    this.preference.set(this.isDark() ? 'light' : 'dark');
  }
}

function readQueryParam(): ThemePreference | null {
  const value = new URLSearchParams(window.location.search).get('theme');
  return value === 'light' || value === 'dark' ? value : null;
}

function readInitialPreference(): ThemePreference {
  const fromQuery = readQueryParam();
  if (fromQuery) {
    return fromQuery;
  }
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === 'light' || stored === 'dark') {
      return stored;
    }
  } catch {
    // storage unavailable
  }
  return 'system';
}
