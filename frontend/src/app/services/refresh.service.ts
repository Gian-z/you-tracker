import { Injectable, signal } from '@angular/core';

/**
 * App-wide notification that work logs changed (a worklog was created or
 * drafts were committed). Pages watch `worklogVersion` and refetch.
 */
@Injectable({ providedIn: 'root' })
export class RefreshService {
  private readonly version = signal(0);

  readonly worklogVersion = this.version.asReadonly();

  worklogChanged(): void {
    this.version.update((v) => v + 1);
  }
}
