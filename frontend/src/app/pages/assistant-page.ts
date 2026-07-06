import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DraftReviewDialog } from '../dialogs/draft-review-dialog';
import { addDays, startOfWeek, toIsoDate } from '../format';
import { DraftResult, TriageResult } from '../models';
import { ApiService } from '../services/api.service';

type BusyAction = 'draft' | 'summary-day' | 'summary-week' | 'triage';

@Component({
  selector: 'app-assistant-page',
  imports: [FormsModule, DraftReviewDialog],
  template: `
    <div class="page page-narrow">
      <section class="card">
        <h2>Draft work log</h2>
        <p class="muted">Describe what you did and let Claude match it to your issues.</p>
        <textarea
          rows="5"
          [(ngModel)]="freeText"
          placeholder="Describe your day… e.g. 'morning on the importer bug, 2h; standup 30m; afternoon reviewing PRs for ABC-42'"
          [disabled]="busy() !== null"
        ></textarea>
        <div class="form-row">
          <label class="inline-label">
            Date
            <input type="date" [(ngModel)]="date" [disabled]="busy() !== null" />
          </label>
          <button
            type="button"
            class="primary"
            (click)="draft()"
            [disabled]="busy() !== null || !freeText().trim()"
          >
            Draft work log
          </button>
        </div>
      </section>

      <section class="card">
        <h2>Summaries</h2>
        <div class="form-row">
          <button type="button" (click)="summarize('day')" [disabled]="busy() !== null">Summarize day</button>
          <button type="button" (click)="summarize('week')" [disabled]="busy() !== null">Summarize week</button>
        </div>
        @if (summaryText(); as text) {
          <div class="summary-text">{{ text }}</div>
        }
      </section>

      <section class="card">
        <h2>Triage</h2>
        <div class="form-row">
          <button type="button" (click)="triage()" [disabled]="busy() !== null">Triage</button>
        </div>
        @if (triageResult(); as result) {
          @if (result.focusSuggestion) {
            <div class="focus-suggestion">
              <strong>Focus:</strong> {{ result.focusSuggestion }}
            </div>
          }
          <ol class="triage-list">
            @for (entry of result.ranked; track entry.issueId) {
              <li>
                <div class="triage-head">
                  <span class="rank">#{{ entry.rank }}</span>
                  @if (issueUrl(entry.issueId); as url) {
                    <a [href]="url" target="_blank" rel="noopener" class="issue-id">{{ entry.issueId }}</a>
                  } @else {
                    <span class="issue-id">{{ entry.issueId }}</span>
                  }
                  <span>{{ entry.summary }}</span>
                  <span class="muted small">score {{ entry.score }}</span>
                </div>
                @if (entry.reasons.length > 0) {
                  <ul class="reasons small muted">
                    @for (reason of entry.reasons; track $index) {
                      <li>{{ reason }}</li>
                    }
                  </ul>
                }
              </li>
            }
          </ol>
        }
      </section>

      @if (busy()) {
        <div class="banner info">
          <span class="spinner"></span> Claude is thinking… this can take a minute
        </div>
      }

      @if (error(); as err) {
        <div class="banner error">
          {{ err }}
          <button type="button" class="link" (click)="error.set(null)">dismiss</button>
        </div>
      }
    </div>

    @if (draftResult(); as result) {
      <app-draft-review-dialog
        [drafts]="result.drafts"
        [unmatched]="result.unmatched"
        (closed)="draftResult.set(null)"
      />
    }
  `,
})
export class AssistantPage {
  private readonly api = inject(ApiService);
  private webUrls = new Map<string, string>();

  readonly freeText = signal('');
  readonly date = signal(toIsoDate(new Date()));
  readonly busy = signal<BusyAction | null>(null);
  readonly error = signal<string | null>(null);
  readonly draftResult = signal<DraftResult | null>(null);
  readonly summaryText = signal<string | null>(null);
  readonly triageResult = signal<TriageResult | null>(null);

  constructor() {
    // Cached lookup so triage results can link issue ids; best-effort only.
    this.api
      .getIssues(false)
      .then((issues) => {
        this.webUrls = new Map(issues.map((i) => [i.issueId, i.webUrl]));
      })
      .catch(() => undefined);
  }

  issueUrl(issueId: string): string | null {
    return this.webUrls.get(issueId) ?? null;
  }

  async draft(): Promise<void> {
    await this.run('draft', async () => {
      this.draftResult.set(await this.api.aiDraft(this.freeText().trim(), this.date()));
    });
  }

  async summarize(range: 'day' | 'week'): Promise<void> {
    let from: string;
    let to: string;
    if (range === 'day') {
      from = this.date();
      to = this.date();
    } else {
      const monday = startOfWeek(new Date());
      from = toIsoDate(monday);
      to = toIsoDate(addDays(monday, 6));
    }
    await this.run(range === 'day' ? 'summary-day' : 'summary-week', async () => {
      const result = await this.api.aiSummary(from, to);
      this.summaryText.set(result.text);
    });
  }

  async triage(): Promise<void> {
    await this.run('triage', async () => {
      this.triageResult.set(await this.api.aiTriage());
    });
  }

  private async run(action: BusyAction, work: () => Promise<void>): Promise<void> {
    this.busy.set(action);
    this.error.set(null);
    try {
      await work();
    } catch (err) {
      this.error.set((err as Error).message);
    } finally {
      this.busy.set(null);
    }
  }
}
