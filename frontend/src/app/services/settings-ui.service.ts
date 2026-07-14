import { Injectable, signal } from '@angular/core';

export type SettingsTab = 'allg' | 'yt' | 'ki' | 'cal' | 'presets' | 'team';

/** Opens the settings dialog (hosted once in the app shell) from anywhere, on a given tab. */
@Injectable({ providedIn: 'root' })
export class SettingsUiService {
  readonly open = signal(false);
  readonly tab = signal<SettingsTab>('allg');

  show(tab: SettingsTab = 'allg'): void {
    this.tab.set(tab);
    this.open.set(true);
  }
}
