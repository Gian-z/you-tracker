import { Component, computed, inject, signal } from '@angular/core';
import { formatClock, formatDayLabel, formatDuration, toIsoDate } from '../format';
import {
  AmpelStatus,
  FeatureDeviation,
  SprintDashboard,
  SprintVerdict,
  TeamConfig,
  TeamSprint,
} from '../models';
import { ApiService } from '../services/api.service';
import { DevService } from '../services/dev.service';
import { SettingsUiService } from '../services/settings-ui.service';

const AMPEL_ICON: Record<AmpelStatus, string> = {
  onTrack: '✅',
  achtung: '⚠️',
  problem: '🔴',
  abwesend: '⬜',
};

const AMPEL_STATUS: Record<AmpelStatus, string> = {
  onTrack: 'On Track',
  achtung: 'Achtung',
  problem: 'Problem',
  abwesend: 'Abwesend',
};

const AMPEL_BADGE: Record<AmpelStatus, string> = {
  onTrack: 'green',
  achtung: 'amber',
  problem: 'red',
  abwesend: 'neutral',
};

const AMPEL_COLOR: Record<AmpelStatus, string> = {
  onTrack: 'var(--ok)',
  achtung: 'var(--warn)',
  problem: 'var(--danger)',
  abwesend: 'var(--muted)',
};

const AMPEL_ORDER: Record<AmpelStatus, number> = {
  onTrack: 0,
  achtung: 1,
  problem: 2,
  abwesend: 3,
};

/** Scrum-Master sprint dashboard: heatmap, roadmap gap, estimation deviations, verdicts. */
@Component({
  selector: 'app-sprint-page',
  template: `
    <div class="page sprint">
      <div style="display: flex; align-items: baseline; gap: 0.9rem; flex-wrap: wrap; margin-bottom: var(--space-2)">
        <h1 style="margin: 0; font-size: 1.25rem; font-weight: 600">Sprint {{ sprintName() }}</h1>
        @if (sprintMeta(); as m) {
          <span class="muted small">
            {{ m.range }} · Tag {{ m.day }} von {{ m.total }} · Team-Ansicht (Scrum Master)
          </span>
        }
        <span class="flex-spacer"></span>
        <button type="button" (click)="settingsUi.show('team')">Sprints &amp; Absenzen verwalten</button>
      </div>

      <div class="toolbar">
        <label class="inline">
          Sprint
          <!-- Native binding (kein ngModel): @for-Optionen materialisieren asynchron und
               verpassen sonst den initialen writeValue — Picker erschien leer. -->
          <select
            [value]="sprintName()"
            (change)="selectSprint($any($event.target).value)"
            aria-label="Sprint"
          >
            @for (s of sortedSprints(); track s.name) {
              <option [value]="s.name" [selected]="s.name === sprintName()">{{ s.name }}</option>
            }
          </select>
        </label>
        <button type="button" (click)="load(true)" [disabled]="loading()">Aktualisieren</button>
        <span class="flex-spacer"></span>
        <button type="button" (click)="generateVerdicts()" [disabled]="aiBusy() || loading() || !dashboard()">
          AI: Fazit generieren
        </button>
      </div>

      @if (error(); as err) {
        <div class="banner error">
          {{ err }}
          <button type="button" class="link" (click)="error.set(null)">schliessen</button>
        </div>
      }
      @if (aiBusy()) {
        <div class="banner info"><span class="spinner"></span> Claude schreibt das Fazit… das kann eine Minute dauern</div>
      }

      @if (loading()) {
        <div class="loading"><span class="spinner"></span> Sprint-Daten laden…</div>
      } @else if (dashboard(); as d) {
        <!-- S1+S2: heatmap | roadmap ist/soll (1.5fr/1fr, stacks when narrow) -->
        <div style="display: flex; flex-wrap: wrap; gap: var(--space-3); align-items: stretch; margin-bottom: var(--space-3)">
          <!-- heatmap -->
          <section class="card" style="flex: 1.5 1 34rem; min-width: 0; margin: 0">
            <div style="display: flex; align-items: baseline; justify-content: space-between; gap: var(--space-2); flex-wrap: wrap">
              <h2>Buchungs-Heatmap</h2>
              <span class="legend small muted" style="margin-top: 0">
                <span class="heat-chip heat-reached">≥ 95%</span>
                <span class="heat-chip heat-partial">teilweise</span>
                <span class="heat-chip heat-none">leer</span>
                <span class="heat-chip heat-off">abwesend</span>
              </span>
            </div>
            <div class="table-scroll">
              <table class="heatmap-table">
                <thead>
                  <tr>
                    <th class="sticky-col">Dev</th>
                    @for (day of d.workdays; track day; let i = $index) {
                      <th class="num" [class.heat-today]="day === todayIso" [title]="shortDay(day)">D{{ i + 1 }}</th>
                    }
                    <th class="num">Σ</th>
                  </tr>
                </thead>
                <tbody>
                  @for (row of d.heatmap; track row.login) {
                    <tr>
                      <td class="sticky-col" [title]="row.login">{{ row.name }}{{ isMe(row.login) ? ' (du)' : '' }}</td>
                      @for (cell of row.cells; track cell.date) {
                        <td class="heat num" [class]="'heat-' + cell.state" [title]="cell.date + ' · ' + dur(cell.minutes)">
                          {{ cell.minutes > 0 ? clock(cell.minutes) : (cell.state === 'off' ? '–' : '') }}
                        </td>
                      }
                      <td class="num total">{{ clock(row.totalMinutes) }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </section>

          <!-- roadmap ist/soll + top estimation misses -->
          <section class="card" style="flex: 1 1 22rem; min-width: 0; margin: 0">
            <h2>Roadmap-Stunden — Soll vs. Ist</h2>
            @for (g of d.gaps; track g.login) {
              <div style="margin-bottom: 0.55rem" [title]="g.login">
                <div style="display: flex; justify-content: space-between; gap: 0.6rem; font-size: 0.88rem; margin-bottom: 2px">
                  <span style="overflow: hidden; text-overflow: ellipsis; white-space: nowrap">
                    {{ g.name }}{{ isMe(g.login) ? ' (du)' : '' }}
                  </span>
                  <span
                    class="num"
                    style="font-weight: 600; white-space: nowrap"
                    [style.color]="attainColor(g.attainmentPercent)"
                    [title]="g.attainmentPercent + '% · ' + dur(g.roadmapMinutes) + ' / ' + dur(g.targetMinutes)"
                  >
                    {{ hours(g.roadmapMinutes) }}h / {{ hours(g.targetMinutes) }}h
                  </span>
                </div>
                <span class="meter" style="display: block; height: 7px">
                  <span
                    class="meter-fill"
                    [class.ok]="g.attainmentPercent >= 95"
                    [class.warn]="g.attainmentPercent >= 75 && g.attainmentPercent < 95"
                    [class.danger]="g.attainmentPercent < 75"
                    [style.width.%]="Math.min(100, g.attainmentPercent)"
                  ></span>
                </span>
              </div>
            }

            <div style="margin-top: 0.8rem; padding-top: 0.7rem; border-top: 1px solid var(--border)">
              <div class="small muted" style="font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em; margin-bottom: 0.35rem">
                Grösste Schätzfehler
              </div>
              @for (m of topMisses(); track m.issueId) {
                <div style="display: flex; justify-content: space-between; gap: 0.6rem; font-size: 0.88rem; padding: 2px 0">
                  <span style="overflow: hidden; text-overflow: ellipsis; white-space: nowrap" [title]="m.issueId + ' ' + m.summary">
                    <span class="issue-id">{{ m.issueId }}</span> {{ m.summary }}
                  </span>
                  <span class="num" style="color: var(--danger); font-weight: 600; white-space: nowrap">{{ factor(m) }}×</span>
                </div>
              } @empty {
                <div class="muted small">Keine Schätzabweichungen.</div>
              }
              <button type="button" class="link small" (click)="showAllDeviations.set(!showAllDeviations())">
                {{ showAllDeviations() ? 'Ausblenden' : 'Alle anzeigen' }}
              </button>
            </div>

            @if (showAllDeviations()) {
              <div class="table-scroll" style="margin-top: var(--space-2)">
                <table class="data-table compact">
                  <thead>
                    <tr><th>Feature</th><th>Assignee</th><th class="num">Schätzung</th><th class="num">Gebucht</th><th class="num">Gap</th></tr>
                  </thead>
                  <tbody>
                    @for (f of d.deviations; track f.issueId) {
                      <tr>
                        <td>
                          <span class="issue-id">{{ f.issueId }}</span>
                          <span class="summary-cell deviation-summary">{{ f.summary }}</span>
                        </td>
                        <td class="nowrap">{{ f.assigneeLogin ?? '–' }}</td>
                        <td class="num nowrap">{{ f.estimateMinutes !== null ? dur(f.estimateMinutes) : '–' }}</td>
                        <td class="num nowrap">{{ dur(f.spentMinutes) }}</td>
                        <td class="num nowrap" [class.over]="f.gapMinutes > 0" [class.under]="f.gapMinutes < 0">
                          {{ f.gapMinutes > 0 ? '+' : '' }}{{ dur2(f.gapMinutes) }}
                          @if (f.gapPercent !== null) {
                            ({{ f.gapPercent > 0 ? '+' : '' }}{{ f.gapPercent }}%)
                          }
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          </section>
        </div>

        <!-- S3: verdicts -->
        <section class="card">
          <h2>Fazit pro Entwickler</h2>
          <div class="verdict-grid">
            @for (v of sortedVerdicts(); track v.login) {
              <div class="verdict-card" [class]="'ampel-' + v.ampel">
                <div class="verdict-head">
                  <span>{{ icon(v.ampel) }}</span>
                  <strong>{{ v.name }}{{ isMe(v.login) ? ' (du)' : '' }}</strong>
                  <span class="flex-spacer"></span>
                  <span class="badge" [class]="badgeClass(v.ampel)">{{ statusLabel(v.ampel) }}</span>
                </div>
                <div class="verdict-kpis small">
                  <span>
                    Quote
                    <span class="num" style="font-weight: 600" [style.color]="ampelColor(v.ampel)">{{ v.attainmentPercent }}%</span>
                  </span>
                  <span>
                    Roadmap
                    <span class="num" style="font-weight: 600" [title]="'Ziel ' + dur(v.targetMinutes)">{{ clock(v.roadmapMinutes) }}</span>
                  </span>
                  <span class="muted">{{ v.daysWithBookings }}/{{ v.availableDays }} Tage gebucht</span>
                  @if (v.unknownMinutes > 0) {
                    <span class="muted">Unbekannt {{ dur(v.unknownMinutes) }}</span>
                  }
                </div>
                @if (v.signals.length > 0) {
                  <ul class="reasons small muted">
                    @for (s of v.signals; track $index) {
                      <li>{{ s }}</li>
                    }
                  </ul>
                }
                @if (verdictText(v.login); as text) {
                  <div class="verdict-text small"><span class="muted">✦ Fazit:</span> {{ text }}</div>
                }
              </div>
            }
          </div>
        </section>
      } @else {
        <div class="banner">
          Keine Team-Konfiguration gefunden. <code>team.json</code> neben config.json anlegen (siehe README).
        </div>
      }
    </div>
  `,
})
export class SprintPage {
  private readonly api = inject(ApiService);
  private readonly dev = inject(DevService);
  protected readonly settingsUi = inject(SettingsUiService);
  protected readonly Math = Math;
  protected readonly todayIso = toIsoDate(new Date());

  readonly team = signal<TeamConfig | null>(null);
  readonly sprintName = signal<string>('');
  readonly dashboard = signal<SprintDashboard | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly aiBusy = signal(false);
  readonly verdicts = signal<SprintVerdict[]>([]);
  readonly showAllDeviations = signal(false);

  /** Newest sprint first (by last workday) — the picker's natural reading order. */
  readonly sortedSprints = computed(() =>
    [...(this.team()?.sprints ?? [])].sort((a, b) =>
      (lastWorkday(b) ?? '').localeCompare(lastWorkday(a) ?? ''),
    ),
  );

  /** Header subline: "01.07. – 14.07. · Tag 6 von 10" for the selected sprint. */
  readonly sprintMeta = computed(() => {
    const sprint = this.team()?.sprints.find((s) => s.name === this.sprintName());
    if (!sprint || sprint.workdays.length === 0) {
      return null;
    }
    const days = [...sprint.workdays].sort();
    const total = days.length;
    const passed = days.filter((d) => d <= this.todayIso).length;
    return {
      range: `${shortDate(days[0])} – ${shortDate(days[total - 1])}`,
      day: Math.min(Math.max(passed, 1), total),
      total,
    };
  });

  /** Verdict cards ordered ok → achtung → problem → abwesend. */
  readonly sortedVerdicts = computed(() =>
    [...(this.dashboard()?.verdicts ?? [])].sort(
      (a, b) => AMPEL_ORDER[a.ampel] - AMPEL_ORDER[b.ampel],
    ),
  );

  /** Top-3 estimation misses (worst overrun factor first). Only real overruns (>1×). */
  readonly topMisses = computed(() =>
    (this.dashboard()?.deviations ?? [])
      .filter(
        (f) =>
          f.estimateMinutes !== null &&
          f.estimateMinutes > 0 &&
          f.spentMinutes > f.estimateMinutes,
      )
      .sort(
        (a, b) =>
          Math.abs(b.gapPercent ?? 0) - Math.abs(a.gapPercent ?? 0) ||
          Math.abs(b.gapMinutes) - Math.abs(a.gapMinutes),
      )
      .slice(0, 3),
  );

  constructor() {
    void this.init();
  }

  private async init(): Promise<void> {
    try {
      const team = await this.api.getTeam();
      this.team.set(team);
      // Prefer the configured active sprint; fall back to latest by last workday.
      const active =
        team?.activeSprint != null && team.sprints.some((s) => s.name === team.activeSprint)
          ? team.activeSprint
          : this.sortedSprints()[0]?.name;
      if (active) {
        this.sprintName.set(active);
        await this.load(false);
      }
    } catch (err) {
      this.error.set((err as Error).message);
    }
  }

  selectSprint(name: string): void {
    this.sprintName.set(name);
    this.verdicts.set([]);
    void this.load(false);
  }

  async load(refresh: boolean): Promise<void> {
    if (!this.sprintName()) {
      return;
    }
    this.loading.set(true);
    this.error.set(null);
    try {
      this.dashboard.set(await this.api.getSprintDashboard(this.sprintName(), refresh));
    } catch (err) {
      this.error.set((err as Error).message);
    } finally {
      this.loading.set(false);
    }
  }

  async generateVerdicts(): Promise<void> {
    this.aiBusy.set(true);
    this.error.set(null);
    try {
      this.verdicts.set(await this.api.aiSprintVerdicts(this.sprintName()));
    } catch (err) {
      this.error.set((err as Error).message);
    } finally {
      this.aiBusy.set(false);
    }
  }

  verdictText(login: string): string | null {
    return this.verdicts().find((v) => v.login.toLowerCase() === login.toLowerCase())?.text ?? null;
  }

  isMe(login: string): boolean {
    const me = this.dev.currentUser()?.login;
    return me !== undefined && me.toLowerCase() === login.toLowerCase();
  }

  shortDay(iso: string): string {
    return formatDayLabel(iso);
  }

  icon(status: AmpelStatus): string {
    return AMPEL_ICON[status];
  }

  statusLabel(status: AmpelStatus): string {
    return AMPEL_STATUS[status];
  }

  badgeClass(status: AmpelStatus): string {
    return AMPEL_BADGE[status];
  }

  ampelColor(status: AmpelStatus): string {
    return AMPEL_COLOR[status];
  }

  /** Ist/Soll text color: >=95% ok, >=75% amber, else red — mirrors the meter fill. */
  attainColor(percent: number): string {
    return percent >= 95 ? 'var(--ok)' : percent >= 75 ? 'var(--warn)' : 'var(--danger)';
  }

  hours(minutes: number): number {
    return Math.round(minutes / 60);
  }

  /** Overrun factor "spent / estimate" with one decimal, e.g. 2.6. */
  factor(f: FeatureDeviation): number {
    return Math.round((f.spentMinutes / (f.estimateMinutes ?? 1)) * 10) / 10;
  }

  clock(minutes: number): string {
    return formatClock(minutes);
  }

  dur(minutes: number): string {
    return formatDuration(minutes);
  }

  dur2(minutes: number): string {
    return formatDuration(Math.abs(minutes)) === '0m' && minutes < 0
      ? '0m'
      : (minutes < 0 ? '−' : '') + formatDuration(Math.abs(minutes));
  }
}

/** ISO workdays sort lexicographically — the max is the sprint's last day. */
function lastWorkday(sprint: TeamSprint): string | null {
  return sprint.workdays.length > 0 ? [...sprint.workdays].sort().at(-1)! : null;
}

/** "01.07." for a "yyyy-MM-dd" string (mockup's compact range format). */
function shortDate(iso: string): string {
  return `${iso.slice(8, 10)}.${iso.slice(5, 7)}.`;
}
