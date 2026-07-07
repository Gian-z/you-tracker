import { Injectable, signal } from '@angular/core';

/** Open/close state of the global ticket search — the dialog is hosted once in the app shell. */
@Injectable({ providedIn: 'root' })
export class SearchService {
  readonly open = signal(false);
}
