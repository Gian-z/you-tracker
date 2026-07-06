import { Injectable, computed, inject, signal } from '@angular/core';
import { UserInfo } from '../models';
import { ApiService } from './api.service';

const STORAGE_KEY = 'you-tracker.dev';

/** Which developer's tickets/bookings the app is showing. Writes always stay the token owner. */
@Injectable({ providedIn: 'root' })
export class DevService {
  private readonly api = inject(ApiService);

  readonly currentUser = signal<UserInfo | null>(null);
  readonly users = signal<UserInfo[]>([]);
  readonly selectedLogin = signal<string>(localStorage.getItem(STORAGE_KEY) ?? '');

  /** True while the selection is the logged-in user (or nothing is selected yet). */
  readonly isSelf = computed(() => {
    const me = this.currentUser()?.login ?? '';
    const selected = this.selectedLogin();
    return selected === '' || selected.toLowerCase() === me.toLowerCase();
  });

  /** Value for API `dev` parameters: null when viewing yourself. */
  readonly devParam = computed(() => (this.isSelf() ? null : this.selectedLogin()));

  readonly selectedName = computed(() => {
    const login = this.selectedLogin();
    const match = this.users().find((u) => u.login.toLowerCase() === login.toLowerCase());
    return match?.fullName ?? login;
  });

  async init(): Promise<void> {
    try {
      const meta = await this.api.getMeta();
      this.currentUser.set(meta.currentUser);
      if (!this.selectedLogin()) {
        this.selectedLogin.set(meta.currentUser.login);
      }
    } catch {
      // meta failure surfaces on the pages themselves
    }
    try {
      this.users.set(await this.api.getUsers());
    } catch {
      this.users.set([]); // dropdown degrades to text input
    }
  }

  select(login: string): void {
    const value = login.trim();
    const effective = value === '' ? (this.currentUser()?.login ?? '') : value;
    this.selectedLogin.set(effective);
    localStorage.setItem(STORAGE_KEY, effective);
  }
}
