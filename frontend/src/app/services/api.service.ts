import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import {
  CommitResult,
  DraftResult,
  Meta,
  TaskListItem,
  TimeOverview,
  TimerState,
  TimerStopResult,
  TriageResult,
  WorkItem,
  WorkLogDraft,
  WorkLogRequest,
  WorkType,
} from '../models';

export function extractErrorMessage(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    const payload: unknown = err.error;
    if (payload && typeof payload === 'object' && typeof (payload as { error?: unknown }).error === 'string') {
      return (payload as { error: string }).error;
    }
    if (typeof payload === 'string' && payload.length > 0) {
      try {
        const parsed = JSON.parse(payload) as { error?: string };
        if (parsed?.error) {
          return parsed.error;
        }
      } catch {
        // not JSON — fall through
      }
    }
    if (err.status === 0) {
      return 'Cannot reach the backend. Is the API running?';
    }
    return `Request failed (${err.status} ${err.statusText})`;
  }
  return err instanceof Error ? err.message : String(err);
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  getMeta(): Promise<Meta> {
    return this.get<Meta>('/api/meta');
  }

  getIssues(refresh = false): Promise<TaskListItem[]> {
    return this.get<TaskListItem[]>('/api/issues', { refresh: String(refresh) });
  }

  getOverview(from: string, to: string, refresh = false): Promise<TimeOverview> {
    return this.get<TimeOverview>('/api/time/overview', { from, to, refresh: String(refresh) });
  }

  getWorkTypes(): Promise<WorkType[]> {
    return this.get<WorkType[]>('/api/worktypes');
  }

  getTimer(): Promise<TimerState | null> {
    return this.get<TimerState | null>('/api/timer');
  }

  startTimer(issueId: string, issueSummary: string): Promise<TimerState> {
    return this.post<TimerState>('/api/timer/start', { issueId, issueSummary });
  }

  stopTimer(): Promise<TimerStopResult | null> {
    return this.post<TimerStopResult | null>('/api/timer/stop', {});
  }

  createWorklog(request: WorkLogRequest): Promise<WorkItem> {
    return this.post<WorkItem>('/api/worklog', request);
  }

  commitDrafts(drafts: WorkLogDraft[], defaultTypeId: string | null = null): Promise<CommitResult> {
    return this.post<CommitResult>('/api/worklog/commit', { drafts, defaultTypeId });
  }

  aiDraft(freeText: string, date: string): Promise<DraftResult> {
    return this.post<DraftResult>('/api/ai/draft', { freeText, date });
  }

  aiGapfills(from: string, to: string): Promise<DraftResult> {
    return this.post<DraftResult>('/api/ai/gapfills', { from, to });
  }

  aiSummary(from: string, to: string): Promise<{ text: string }> {
    return this.post<{ text: string }>('/api/ai/summary', { from, to });
  }

  aiTriage(): Promise<TriageResult> {
    return this.post<TriageResult>('/api/ai/triage', {});
  }

  private get<T>(url: string, params?: Record<string, string>): Promise<T> {
    return this.unwrap(this.http.get<T>(url, { params }));
  }

  private post<T>(url: string, body: unknown): Promise<T> {
    return this.unwrap(this.http.post<T>(url, body));
  }

  private async unwrap<T>(response$: Observable<T>): Promise<T> {
    try {
      return await firstValueFrom(response$);
    } catch (err) {
      throw new Error(extractErrorMessage(err));
    }
  }
}
