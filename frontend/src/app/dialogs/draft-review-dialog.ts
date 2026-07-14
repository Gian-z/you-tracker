import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TicketPicker } from '../components/ticket-picker';
import { formatDuration, formatShortDate, parseDuration, toIsoDate } from '../format';
import { CommitResult, TaskListItem, UnmatchedItem, WorkLogDraft } from '../models';
import { ApiService } from '../services/api.service';
import { DevService } from '../services/dev.service';
import { RefreshService } from '../services/refresh.service';

interface DraftRow {
  draft: WorkLogDraft;
  checked: boolean;
  duration: string;
  comment: string;
  /** True for rows created by assigning an unmatched item — shown as "manuell" badge. */
  manual: boolean;
}

@Component({
  selector: 'app-draft-review-dialog',
  imports: [FormsModule, TicketPicker],
  host: { '(document:keydown.escape)': 'cancel()' },
  template: `
    <div class="overlay" (click)="cancel()">
      <div class="dialog dialog-wide" role="dialog" aria-label="Entwürfe prüfen" (click)="$event.stopPropagation()">
        <div class="dialog-head-row">
          <h2>✦ Entwürfe prüfen</h2>
          <button type="button" class="icon" (click)="cancel()" aria-label="Schliessen">✕</button>
        </div>

        @if (result(); as res) {
          <div class="commit-result">
            @if (res.created > 0) {
              <div class="banner success">{{ res.created }} {{ res.created === 1 ? 'Buchung' : 'Buchungen' }} erstellt.</div>
            } @else {
              <div class="banner">Keine Buchungen erstellt.</div>
            }
            @for (note of res.notes; track $index) {
              <div class="banner info">{{ note }}</div>
            }
            @for (err of res.errors; track $index) {
              <div class="banner error">{{ err }}</div>
            }
          </div>
          <div class="dialog-actions">
            <button type="button" class="primary" (click)="closed.emit()">Schliessen</button>
          </div>
        } @else {
          <div class="muted small" style="margin-bottom: 0.85rem;">
            Nur angehakte Zeilen werden geschrieben — nichts erreicht YouTrack ohne deine Bestätigung.
          </div>

          @if (rows().length > 0) {
            <div style="display: flex; flex-direction: column; gap: 0.5rem; margin-bottom: 0.85rem; max-height: 50vh; overflow-y: auto;">
              @for (row of rows(); track $index) {
                <div
                  style="display: flex; gap: 0.7rem; background: var(--surface-2); border: 1px solid var(--border); border-radius: 10px; padding: 0.7rem 0.8rem;"
                  [style.opacity]="row.checked ? null : 0.55"
                >
                  <input
                    type="checkbox"
                    style="margin-top: 3px;"
                    [ngModel]="row.checked"
                    (ngModelChange)="setChecked($index, $event)"
                    [attr.aria-label]="'Übernehmen: ' + row.draft.issueId"
                  />
                  <div style="flex: 1; min-width: 0;">
                    <div style="display: flex; align-items: center; gap: 0.55rem; margin-bottom: 0.25rem; min-width: 0;">
                      <app-ticket-picker
                        [issueId]="row.draft.issueId"
                        [issueSummary]="row.draft.issueSummary"
                        (picked)="onRowTicketPicked($index, $event)"
                      />
                      <span class="muted small" style="flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">
                        {{ row.draft.issueSummary }}
                      </span>
                      <input
                        type="text"
                        class="num"
                        style="width: 4.2rem; text-align: center;"
                        [ngModel]="row.duration"
                        (ngModelChange)="setDuration($index, $event)"
                        [disabled]="!row.checked"
                        placeholder="1h 30m"
                        [attr.aria-label]="'Dauer: ' + row.draft.issueId"
                      />
                      <span class="muted small nowrap">{{ shortDate(row.draft.date) }}</span>
                      @if (row.draft.workTypeName; as typeName) {
                        <span class="muted small nowrap">{{ typeName }}</span>
                      }
                      @if (row.manual) {
                        <span class="badge neutral">manuell</span>
                      } @else {
                        <span class="badge" [class]="confidenceClass(row.draft.confidence)">
                          {{ confidenceLabel(row.draft.confidence) }}
                        </span>
                      }
                    </div>
                    <input
                      type="text"
                      style="width: 100%;"
                      [ngModel]="row.comment"
                      (ngModelChange)="setComment($index, $event)"
                      [disabled]="!row.checked"
                      placeholder="Kommentar"
                      [attr.aria-label]="'Kommentar: ' + row.draft.issueId"
                    />
                    @if (row.draft.reasoning) {
                      <div class="muted small" [title]="row.draft.reasoning" style="margin-top: 0.25rem;">
                        {{ row.draft.reasoning }}
                      </div>
                    }
                  </div>
                </div>
              }
            </div>
          } @else {
            <div class="banner">No drafts were produced.</div>
          }

          @if (unmatchedRows().length > 0) {
            <div class="unmatched">
              <h3>Nicht zugeordnet</h3>
              @for (item of unmatchedRows(); track $index) {
                <div
                  class="unmatched-item"
                  style="display: flex; align-items: center; gap: 0.7rem; border: 1px dashed var(--border); border-radius: 9px; padding: 0.55rem 0.8rem; margin-bottom: 0.4rem;"
                >
                  <span style="flex: 1; min-width: 0;">
                    <span style="display: block;">{{ item.text }}</span>
                    <span class="muted small">{{ item.reason }}</span>
                  </span>
                  <app-ticket-picker
                    [placeholder]="'Ticket zuweisen'"
                    (picked)="assignUnmatched($index, $event)"
                  />
                </div>
              }
            </div>
          }

          @if (error(); as err) {
            <div class="banner error">{{ err }}</div>
          }

          @if (!dev.isSelf()) {
            <div class="banner">
              Buchungen werden immer als DU erstellt — wechsle zurück zu dir selbst, um zu buchen.
            </div>
          }
          <div class="dialog-actions">
            <button type="button" class="secondary" (click)="cancel()" [disabled]="committing()">Verwerfen</button>
            <button
              type="button"
              class="primary"
              (click)="commit()"
              [disabled]="committing() || checkedCount() === 0 || !dev.isSelf()"
              style="background: var(--green-strong); border-color: var(--green-strong); color: #08140b;"
            >
              @if (committing()) {
                <span class="spinner"></span>
              }
              {{ checkedCount() }} {{ checkedCount() === 1 ? 'Buchung' : 'Buchungen' }} erstellen
            </button>
          </div>
        }
      </div>
    </div>
  `,
})
export class DraftReviewDialog {
  private readonly api = inject(ApiService);
  private readonly refresh = inject(RefreshService);
  protected readonly dev = inject(DevService);

  readonly drafts = input.required<WorkLogDraft[]>();
  readonly unmatched = input<UnmatchedItem[]>([]);

  readonly closed = output<void>();
  readonly committed = output<CommitResult>();

  readonly rows = signal<DraftRow[]>([]);
  /** Local editable copy — assigning a ticket moves the item into rows(). */
  readonly unmatchedRows = signal<UnmatchedItem[]>([]);
  readonly committing = signal(false);
  readonly error = signal<string | null>(null);
  readonly result = signal<CommitResult | null>(null);

  readonly checkedCount = computed(() => this.rows().filter((r) => r.checked).length);

  constructor() {
    effect(() => {
      this.rows.set(
        this.drafts().map((draft) => ({
          draft,
          checked: true,
          duration: formatDuration(draft.minutes),
          comment: draft.comment ?? '',
          manual: false,
        })),
      );
    });
    effect(() => {
      this.unmatchedRows.set([...this.unmatched()]);
    });
  }

  setChecked(index: number, checked: boolean): void {
    this.rows.update((rows) => rows.map((row, i) => (i === index ? { ...row, checked } : row)));
  }

  setDuration(index: number, duration: string): void {
    this.rows.update((rows) => rows.map((row, i) => (i === index ? { ...row, duration } : row)));
  }

  setComment(index: number, comment: string): void {
    this.rows.update((rows) => rows.map((row, i) => (i === index ? { ...row, comment } : row)));
  }

  /** Reassign a draft to another ticket (the chip picker). */
  onRowTicketPicked(index: number, picked: TaskListItem): void {
    this.rows.update((rows) =>
      rows.map((row, i) =>
        i === index
          ? { ...row, draft: { ...row.draft, issueId: picked.issueId, issueSummary: picked.summary } }
          : row,
      ),
    );
  }

  /** Turn an unmatched item into a new checked draft row (badge "manuell"). */
  assignUnmatched(index: number, picked: TaskListItem): void {
    const item = this.unmatchedRows()[index];
    if (!item) {
      return;
    }
    const draft: WorkLogDraft = {
      issueId: picked.issueId,
      issueSummary: picked.summary,
      confidence: 'low',
      date: toIsoDate(new Date()),
      minutes: 60,
      workTypeName: null,
      comment: item.text,
      reasoning: `Manuell zugewiesen — ${item.reason}`,
    };
    this.rows.update((rows) => [
      ...rows,
      { draft, checked: true, duration: formatDuration(draft.minutes), comment: item.text, manual: true },
    ]);
    this.unmatchedRows.update((rows) => rows.filter((_, i) => i !== index));
  }

  confidenceClass(confidence: string): string {
    switch (confidence) {
      case 'high':
        return 'badge green';
      case 'medium':
        return 'badge amber';
      default:
        return 'badge red';
    }
  }

  confidenceLabel(confidence: string): string {
    switch (confidence) {
      case 'high':
        return 'hoch';
      case 'medium':
        return 'mittel';
      default:
        return 'niedrig';
    }
  }

  shortDate(iso: string): string {
    return formatShortDate(iso);
  }

  cancel(): void {
    if (this.committing()) {
      return;
    }
    if (this.result()) {
      this.closed.emit();
      return;
    }
    this.closed.emit();
  }

  async commit(): Promise<void> {
    const selected = this.rows()
      .filter((row) => row.checked)
      .map((row) => ({
        ...row.draft,
        minutes: parseDuration(row.duration) ?? row.draft.minutes,
        comment: row.comment.trim() || null,
      }));
    if (selected.length === 0) {
      return;
    }
    this.committing.set(true);
    this.error.set(null);
    try {
      const result = await this.api.commitDrafts(selected, null);
      this.result.set(result);
      if (result.created > 0) {
        this.refresh.worklogChanged();
      }
      this.committed.emit(result);
    } catch (err) {
      this.error.set((err as Error).message);
    } finally {
      this.committing.set(false);
    }
  }
}
