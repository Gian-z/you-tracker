import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { formatShortDate } from '../format';
import { CommitResult, UnmatchedItem, WorkLogDraft } from '../models';
import { ApiService } from '../services/api.service';
import { RefreshService } from '../services/refresh.service';

interface DraftRow {
  draft: WorkLogDraft;
  checked: boolean;
  minutes: number;
  comment: string;
}

@Component({
  selector: 'app-draft-review-dialog',
  imports: [FormsModule],
  host: { '(document:keydown.escape)': 'cancel()' },
  template: `
    <div class="overlay" (click)="cancel()">
      <div class="dialog dialog-wide" role="dialog" aria-label="Review drafts" (click)="$event.stopPropagation()">
        <h2>Review drafts</h2>

        @if (result(); as res) {
          <div class="commit-result">
            @if (res.created > 0) {
              <div class="banner success">Created {{ res.created }} work log {{ res.created === 1 ? 'entry' : 'entries' }}.</div>
            } @else {
              <div class="banner">No entries created.</div>
            }
            @for (err of res.errors; track $index) {
              <div class="banner error">{{ err }}</div>
            }
          </div>
          <div class="dialog-actions">
            <button type="button" class="primary" (click)="closed.emit()">Close</button>
          </div>
        } @else {
          @if (rows().length > 0) {
            <div class="table-scroll">
              <table class="data-table drafts-table">
                <thead>
                  <tr>
                    <th></th>
                    <th>Issue</th>
                    <th>Date</th>
                    <th>Minutes</th>
                    <th>Type</th>
                    <th>Comment</th>
                    <th>Confidence</th>
                  </tr>
                </thead>
                <tbody>
                  @for (row of rows(); track $index) {
                    <tr [class.unchecked]="!row.checked">
                      <td>
                        <input
                          type="checkbox"
                          [ngModel]="row.checked"
                          (ngModelChange)="setChecked($index, $event)"
                          [attr.aria-label]="'Include ' + row.draft.issueId"
                        />
                      </td>
                      <td>
                        <span class="issue-id">{{ row.draft.issueId }}</span>
                        <div class="muted small">{{ row.draft.issueSummary }}</div>
                      </td>
                      <td class="nowrap">{{ shortDate(row.draft.date) }}</td>
                      <td>
                        <input type="number" class="minutes-input" min="1" [(ngModel)]="row.minutes" [disabled]="!row.checked" />
                      </td>
                      <td>{{ row.draft.workTypeName ?? '–' }}</td>
                      <td>
                        <input type="text" class="comment-input" [(ngModel)]="row.comment" [disabled]="!row.checked" />
                        @if (row.draft.reasoning) {
                          <div class="muted small" [title]="row.draft.reasoning">{{ row.draft.reasoning }}</div>
                        }
                      </td>
                      <td>
                        <span class="badge" [class]="confidenceClass(row.draft.confidence)">{{ row.draft.confidence }}</span>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          } @else {
            <div class="banner">No drafts were produced.</div>
          }

          @if (unmatched().length > 0) {
            <div class="unmatched">
              <h3>Unmatched</h3>
              @for (item of unmatched(); track $index) {
                <div class="unmatched-item">
                  <span>{{ item.text }}</span>
                  <span class="muted">— {{ item.reason }}</span>
                </div>
              }
            </div>
          }

          @if (error(); as err) {
            <div class="banner error">{{ err }}</div>
          }

          <div class="dialog-actions">
            <button type="button" class="secondary" (click)="cancel()" [disabled]="committing()">Cancel</button>
            <button type="button" class="primary" (click)="commit()" [disabled]="committing() || checkedCount() === 0">
              @if (committing()) {
                <span class="spinner"></span>
              }
              Commit {{ checkedCount() }} {{ checkedCount() === 1 ? 'entry' : 'entries' }}
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

  readonly drafts = input.required<WorkLogDraft[]>();
  readonly unmatched = input<UnmatchedItem[]>([]);

  readonly closed = output<void>();
  readonly committed = output<CommitResult>();

  readonly rows = signal<DraftRow[]>([]);
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
          minutes: draft.minutes,
          comment: draft.comment ?? '',
        })),
      );
    });
  }

  setChecked(index: number, checked: boolean): void {
    this.rows.update((rows) => rows.map((row, i) => (i === index ? { ...row, checked } : row)));
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
        minutes: Math.max(1, Math.round(row.minutes) || row.draft.minutes),
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
