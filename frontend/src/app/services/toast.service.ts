import { Injectable, signal } from '@angular/core';

export interface Toast {
  id: number;
  text: string;
}

const TOAST_MS = 4000;

/** Transient success feedback (e.g. "XBOX-587 · 1h 30m gebucht"); outlet lives in the app shell. */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private nextId = 1;

  readonly toasts = signal<Toast[]>([]);

  show(text: string): void {
    const toast: Toast = { id: this.nextId++, text };
    this.toasts.update((list) => [...list, toast]);
    setTimeout(() => this.dismiss(toast.id), TOAST_MS);
  }

  dismiss(id: number): void {
    this.toasts.update((list) => list.filter((t) => t.id !== id));
  }
}
