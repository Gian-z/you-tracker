export interface UserInfo {
  login: string;
  fullName: string;
}

export interface Meta {
  targetMinutesPerWorkday: number;
  timezone: string;
  webBaseUrl: string;
  aiProvider: 'anthropic' | 'claude-cli';
  currentUser: UserInfo;
}

export interface BookingPreset {
  id: string;
  name: string;
  issueId: string;
  issueSummary: string;
  minutes: number;
  typeId: string | null;
  typeName: string | null;
  comment: string | null;
}

export interface TaskListItem {
  issueId: string;
  summary: string;
  projectKey: string;
  type: string | null;
  state: string | null;
  priority: string | null;
  estimate: string | null;
  spent: string | null;
  updated: string;
  webUrl: string;
}

export interface WorkItem {
  id: string;
  issueId: string;
  issueSummary: string;
  date: string;
  minutes: number;
  typeId: string | null;
  typeName: string | null;
  text: string | null;
  authorLogin: string | null;
}

export interface DaySummary {
  date: string;
  bookedMinutes: number;
  targetMinutes: number;
  isWorkday: boolean;
  gapMinutes: number;
  fokusScore: number | null;
  contextSwitches: number;
  deepWorkShare: number;
  items: WorkItem[];
}

export interface TimeOverview {
  from: string;
  to: string;
  days: DaySummary[];
  totalBookedMinutes: number;
  totalTargetMinutes: number;
  averageFokusScore: number | null;
}

export interface WorkType {
  id: string;
  name: string;
}

export interface TimerState {
  issueId: string;
  issueSummary: string;
  startedUtc: string;
}

export interface TimerStopResult {
  issueId: string;
  issueSummary: string;
  elapsedMinutes: number;
  date: string;
}

export interface WorkLogRequest {
  issueId: string;
  date: string;
  minutes: number;
  typeId?: string | null;
  text?: string | null;
}

export type Confidence = 'high' | 'medium' | 'low';

export interface WorkLogDraft {
  issueId: string;
  issueSummary: string;
  confidence: Confidence;
  date: string;
  minutes: number;
  workTypeName: string | null;
  comment: string | null;
  reasoning: string | null;
}

export interface UnmatchedItem {
  text: string;
  reason: string;
}

export interface DraftResult {
  drafts: WorkLogDraft[];
  unmatched: UnmatchedItem[];
}

export interface CommitResult {
  created: number;
  errors: string[];
}

export interface TriageEntry {
  issueId: string;
  summary: string;
  rank: number;
  score: number;
  reasons: string[];
}

export interface TriageResult {
  ranked: TriageEntry[];
  focusSuggestion: string;
  sprintSuggestions: TriageEntry[];
}
