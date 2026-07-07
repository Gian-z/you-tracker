import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import {
  BookingPreset,
  BookingTarget,
  CommitResult,
  DraftResult,
  Meta,
  SprintDashboard,
  SprintVerdict,
  TaskListItem,
  TeamAbsence,
  TeamConfig,
  TeamSprint,
  TimeOverview,
  TimerState,
  TimerStopResult,
  TriageResult,
  UserInfo,
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

  getUsers(): Promise<UserInfo[]> {
    return this.get<UserInfo[]>('/api/users');
  }

  getIssues(refresh = false, dev: string | null = null): Promise<TaskListItem[]> {
    return this.get<TaskListItem[]>('/api/issues', this.withDev({ refresh: String(refresh) }, dev));
  }

  searchIssues(q: string, top = 25): Promise<TaskListItem[]> {
    return this.get<TaskListItem[]>('/api/issues/search', { q, top: String(top) });
  }

  getOverview(from: string, to: string, refresh = false, dev: string | null = null): Promise<TimeOverview> {
    return this.get<TimeOverview>(
      '/api/time/overview',
      this.withDev({ from, to, refresh: String(refresh) }, dev),
    );
  }

  getSprintPool(refresh = false, dev: string | null = null): Promise<TaskListItem[]> {
    return this.get<TaskListItem[]>('/api/sprintpool', this.withDev({ refresh: String(refresh) }, dev));
  }

  getTeam(): Promise<TeamConfig | null> {
    return this.get<TeamConfig | null>('/api/team');
  }

  getSprintDashboard(sprint: string, refresh = false): Promise<SprintDashboard> {
    return this.get<SprintDashboard>('/api/sprint/dashboard', { sprint, refresh: String(refresh) });
  }

  saveSprintAbsences(sprintName: string, absences: TeamAbsence[]): Promise<TeamSprint> {
    return this.post<TeamSprint>('/api/sprint/absences', { sprintName, absences });
  }

  aiSprintVerdicts(sprintName: string): Promise<SprintVerdict[]> {
    return this.post<SprintVerdict[]>('/api/ai/sprint-verdicts', { sprintName });
  }

  getPresets(): Promise<BookingPreset[]> {
    return this.get<BookingPreset[]>('/api/presets');
  }

  savePreset(preset: Omit<BookingPreset, 'id'> & { id?: string | null }): Promise<BookingPreset> {
    return this.post<BookingPreset>('/api/presets', preset);
  }

  deletePreset(id: string): Promise<void> {
    return this.unwrap(this.http.delete<void>(`/api/presets/${encodeURIComponent(id)}`));
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

  discardTimer(): Promise<boolean> {
    return this.post<boolean>('/api/timer/discard', {});
  }

  createWorklog(request: WorkLogRequest): Promise<WorkItem> {
    return this.post<WorkItem>('/api/worklog', request);
  }

  getBookingTarget(issueId: string, refresh = false): Promise<BookingTarget> {
    return this.get<BookingTarget>(
      `/api/issues/${encodeURIComponent(issueId)}/booking-target`,
      { refresh: String(refresh) },
    );
  }

  commitDrafts(drafts: WorkLogDraft[], defaultTypeId: string | null = null): Promise<CommitResult> {
    return this.post<CommitResult>('/api/worklog/commit', { drafts, defaultTypeId });
  }

  aiDraft(freeText: string, date: string, dev: string | null = null): Promise<DraftResult> {
    return this.post<DraftResult>('/api/ai/draft', { freeText, date, dev });
  }

  aiGapfills(from: string, to: string, dev: string | null = null): Promise<DraftResult> {
    return this.post<DraftResult>('/api/ai/gapfills', { from, to, dev });
  }

  aiSummary(from: string, to: string, dev: string | null = null): Promise<{ text: string }> {
    return this.post<{ text: string }>('/api/ai/summary', { from, to, dev });
  }

  aiTriage(dev: string | null = null): Promise<TriageResult> {
    const suffix = dev ? `?dev=${encodeURIComponent(dev)}` : '';
    return this.post<TriageResult>(`/api/ai/triage${suffix}`, {});
  }

  private withDev(params: Record<string, string>, dev: string | null): Record<string, string> {
    return dev ? { ...params, dev } : params;
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
