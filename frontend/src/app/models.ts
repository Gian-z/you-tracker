export interface UserInfo {
  login: string;
  fullName: string;
}

export interface Meta {
  targetMinutesPerWorkday: number;
  timezone: string;
  webBaseUrl: string;
  aiProvider: 'anthropic' | 'claude-cli';
  featureTypes: string[];
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
  /** Explicit user confirmation to book on a Feature ticket despite the task rule. */
  allowFeature?: boolean;
}

// --- booking-target pre-flight ("bookings land on tasks" rule) ---

export type BookingTargetKind = 'direct' | 'redirected' | 'ambiguous' | 'noTask';

export interface SubtaskCandidate {
  issueId: string;
  summary: string;
  resolved: boolean;
}

export interface BookingTarget {
  requestedIssueId: string;
  kind: BookingTargetKind;
  targetIssueId: string;
  targetSummary: string | null;
  targetResolved: boolean;
  candidates: SubtaskCandidate[];
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
  /** Informational messages, e.g. Feature→Task redirects. */
  notes: string[];
}

export interface TriageEntry {
  issueId: string;
  summary: string;
  rank: number;
  score: number;
  reasons: string[];
}

// --- Sprint dashboard (Scrum Master view) ---

export interface TeamMember {
  login: string;
  name: string;
  thresholdMinutes: number;
  weekdays: string[];
}

export interface TeamAbsence {
  login: string;
  from: string;
  to: string;
}

export interface TeamSprint {
  name: string;
  workdays: string[];
  absences: TeamAbsence[];
}

export interface TeamConfig {
  name: string;
  projects: string[];
  taskQuery: string;
  featureSprintQuery: string;
  ceremonyPatterns: string[];
  members: TeamMember[];
  sprints: TeamSprint[];
}

export type HeatCellState = 'reached' | 'partial' | 'low' | 'none' | 'today' | 'off' | 'future';

export interface HeatmapCell {
  date: string;
  minutes: number;
  state: HeatCellState;
}

export interface DevHeatmapRow {
  login: string;
  name: string;
  cells: HeatmapCell[];
  totalMinutes: number;
}

export interface RoadmapGapRow {
  login: string;
  name: string;
  roadmapMinutes: number;
  nonRoadmapMinutes: number;
  unknownMinutes: number;
  targetMinutes: number;
  attainmentPercent: number;
  availableDays: number;
}

export interface FeatureDeviation {
  issueId: string;
  summary: string;
  assigneeLogin: string | null;
  roadmapvorhaben: string | null;
  estimateMinutes: number | null;
  spentMinutes: number;
  gapMinutes: number;
  gapPercent: number | null;
}

export type AmpelStatus = 'onTrack' | 'achtung' | 'problem' | 'abwesend';

export interface DevVerdictFacts {
  login: string;
  name: string;
  ampel: AmpelStatus;
  daysWithBookings: number;
  availableDays: number;
  roadmapMinutes: number;
  targetMinutes: number;
  attainmentPercent: number;
  nonRoadmapMinutes: number;
  unknownMinutes: number;
  signals: string[];
  ownFeatures: FeatureDeviation[];
}

export interface SprintDashboard {
  sprintName: string;
  workdays: string[];
  heatmap: DevHeatmapRow[];
  gaps: RoadmapGapRow[];
  deviations: FeatureDeviation[];
  verdicts: DevVerdictFacts[];
}

export interface SprintVerdict {
  login: string;
  text: string;
}

export interface TriageResult {
  ranked: TriageEntry[];
  focusSuggestion: string;
  sprintSuggestions: TriageEntry[];
}
