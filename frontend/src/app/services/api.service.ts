import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import {
  AppConfig,
  BookingPreset,
  BookingTarget,
  CommitResult,
  DayState,
  DraftResult,
  Meta,
  SaveConfigResult,
  UserSettings,
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

  getSprintIssues(refresh = false): Promise<TaskListItem[]> {
    return this.get<TaskListItem[]>('/api/issues/sprint', { refresh: String(refresh) });
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

  addSprint(name: string, from: string, to: string): Promise<TeamSprint> {
    return this.post<TeamSprint>('/api/sprint/sprints', { name, from, to });
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

  pauseTimer(): Promise<TimerState | null> {
    return this.post<TimerState | null>('/api/timer/pause', {});
  }

  resumeTimer(): Promise<TimerState | null> {
    return this.post<TimerState | null>('/api/timer/resume', {});
  }

  createWorklog(request: WorkLogRequest): Promise<WorkItem> {
    return this.post<WorkItem>('/api/worklog', request);
  }

  updateWorklog(
    issueId: string,
    workItemId: string,
    request: { date: string; minutes: number; typeId?: string | null; text?: string | null },
  ): Promise<WorkItem> {
    return this.put<WorkItem>(
      `/api/worklog/${encodeURIComponent(issueId)}/${encodeURIComponent(workItemId)}`,
      request,
    );
  }

  deleteWorklog(issueId: string, workItemId: string): Promise<void> {
    return this.unwrap(
      this.http.delete<void>(
        `/api/worklog/${encodeURIComponent(issueId)}/${encodeURIComponent(workItemId)}`,
      ),
    );
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

  calendarDrafts(date: string): Promise<DraftResult> {
    return this.post<DraftResult>('/api/calendar/drafts', { date });
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

  // --- settings dialog: app config, user settings, per-day presence state, team config ---

  getConfig(): Promise<AppConfig> {
    return this.get<AppConfig>('/api/config');
  }

  saveConfig(config: AppConfig): Promise<SaveConfigResult> {
    return this.put<SaveConfigResult>('/api/config', config);
  }

  testYouTrack(baseUrl: string, token: string): Promise<{ message: string }> {
    return this.post<{ message: string }>('/api/config/test/youtrack', { baseUrl, token });
  }

  testAi(apiKey: string, model: string, cliCommand: string | null = null): Promise<{ message: string }> {
    return this.post<{ message: string }>('/api/config/test/ai', { apiKey, model, cliCommand });
  }

  testCalendar(icsUrl: string): Promise<{ message: string }> {
    return this.post<{ message: string }>('/api/config/test/calendar', { icsUrl });
  }

  getSettings(): Promise<UserSettings> {
    return this.get<UserSettings>('/api/settings');
  }

  saveSettings(settings: UserSettings): Promise<UserSettings> {
    return this.put<UserSettings>('/api/settings', settings);
  }

  getDayStates(from: string, to: string): Promise<Record<string, DayState>> {
    return this.get<Record<string, DayState>>('/api/day-state', { from, to });
  }

  saveDayState(date: string, state: DayState): Promise<DayState> {
    return this.put<DayState>(`/api/day-state/${encodeURIComponent(date)}`, state);
  }

  saveTeam(team: TeamConfig): Promise<TeamConfig> {
    return this.post<TeamConfig>('/api/team', team);
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

  private put<T>(url: string, body: unknown): Promise<T> {
    return this.unwrap(this.http.put<T>(url, body));
  }

  private async unwrap<T>(response$: Observable<T>): Promise<T> {
    try {
      return await firstValueFrom(response$);
    } catch (err) {
      throw new Error(extractErrorMessage(err));
    }
  }
}
