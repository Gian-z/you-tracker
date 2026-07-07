import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { formatClock, formatDayLabel, formatDuration } from '../format';
import {
  AmpelStatus,
  DevVerdictFacts,
  SprintDashboard,
  SprintVerdict,
  TeamAbsence,
  TeamConfig,
} from '../models';
import { ApiService } from '../services/api.service';

const AMPEL_LABEL: Record<AmpelStatus, string> = {
  onTrack: '✅ On Track',
  achtung: '⚠ Achtung',
  problem: '🔴 Problem',
  abwesend: '⬜ Abwesend',
};

/** Scrum-Master sprint dashboard: heatmap, roadmap gap, estimation deviations, verdicts. */
@Component({
  selector: 'app-sprint-page',
  imports: [FormsModule],
  template: `
    <div class="page sprint">
      <div class="toolbar">
        <label class="inline">
          Sprint
          <select [ngModel]="sprintName()" (ngModelChange)="selectSprint($event)">
            @for (s of team()?.sprints ?? []; track s.name) {
              <option [value]="s.name">{{ s.name }}</option>
            }
          </select>
        </label>
        <button type="button" (click)="load(true)" [disabled]="loading()">Aktualisieren</button>
        <button type="button" (click)="openAbsenceEditor()">
          Abwesenheiten ({{ currentAbsences().length }})
        </button>
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
        <!-- S1: heatmap devs × days -->
        <section class="card">
          <h2>Tagesbuchungen</h2>
          <div class="table-scroll">
            <table class="heatmap-table">
              <thead>
                <tr>
                  <th class="sticky-col">Dev</th>
                  @for (day of d.workdays; track day) {
                    <th class="num">{{ shortDay(day) }}</th>
                  }
                  <th class="num">Σ</th>
                </tr>
              </thead>
              <tbody>
                @for (row of d.heatmap; track row.login) {
                  <tr>
                    <td class="sticky-col" [title]="row.name">{{ row.login }}</td>
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
          <div class="legend small muted">
            <span class="heat-chip heat-reached">≥ Ziel</span>
            <span class="heat-chip heat-partial">50–99%</span>
            <span class="heat-chip heat-low">&lt; 50%</span>
            <span class="heat-chip heat-none">0h</span>
            <span class="heat-chip heat-today">heute</span>
            <span class="heat-chip heat-off">abwesend/frei</span>
          </div>
        </section>

        <!-- S2: roadmap gap -->
        <section class="card">
          <h2>Roadmap-Buchungsgap</h2>
          @for (g of d.gaps; track g.login) {
            <div class="gap-row" [title]="g.name">
              <span class="gap-label">{{ g.login }}</span>
              <span class="meter tall">
                <span
                  class="meter-fill"
                  [class.ok]="g.attainmentPercent >= 100"
                  [style.width.%]="Math.min(100, g.attainmentPercent)"
                ></span>
              </span>
              <span class="gap-value">
                {{ g.attainmentPercent }}% · {{ dur(g.roadmapMinutes) }} / {{ dur(g.targetMinutes) }}
                @if (g.unknownMinutes > 0) {
                  <span class="muted small">(+{{ dur(g.unknownMinutes) }} unbekannt)</span>
                }
              </span>
            </div>
          }
        </section>

        <!-- S3: estimation deviations -->
        <section class="card">
          <h2>Feature-Schätzabweichungen (Top {{ d.deviations.length }})</h2>
          <div class="table-scroll">
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
        </section>

        <!-- S4: verdicts -->
        <section class="card">
          <h2>Fazit pro Entwickler</h2>
          <div class="verdict-grid">
            @for (v of d.verdicts; track v.login) {
              <div class="verdict-card" [class]="'ampel-' + v.ampel">
                <div class="verdict-head">
                  <strong>{{ v.login }}</strong>
                  <span class="muted small">{{ v.name }}</span>
                  <span class="flex-spacer"></span>
                  <span>{{ ampel(v.ampel) }}</span>
                </div>
                <div class="verdict-kpis small">
                  <span>{{ v.daysWithBookings }}/{{ v.availableDays }} Tage gebucht</span>
                  <span>Roadmap {{ v.attainmentPercent }}%</span>
                  <span>{{ dur(v.roadmapMinutes) }} / {{ dur(v.targetMinutes) }}</span>
                  @if (v.unknownMinutes > 0) {
                    <span>Unbekannt {{ dur(v.unknownMinutes) }}</span>
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
                  <div class="verdict-text small">{{ text }}</div>
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

    @if (absenceEditorOpen()) {
      <div class="overlay" (click)="absenceEditorOpen.set(false)">
        <div class="dialog absence-editor" role="dialog" aria-label="Abwesenheiten" (click)="$event.stopPropagation()">
          <h2>Abwesenheiten — {{ sprintName() }}</h2>
          @for (a of editAbsences(); track $index) {
            <div class="absence-row">
              <select [ngModel]="a.login" (ngModelChange)="patchAbsence($index, 'login', $event)">
                @for (m of team()?.members ?? []; track m.login) {
                  <option [value]="m.login">{{ m.login }}</option>
                }
              </select>
              <input type="date" [ngModel]="a.from" (ngModelChange)="patchAbsence($index, 'from', $event)" />
              <input type="date" [ngModel]="a.to" (ngModelChange)="patchAbsence($index, 'to', $event)" />
              <button type="button" class="icon" title="Entfernen" (click)="removeAbsence($index)">×</button>
            </div>
          } @empty {
            <div class="muted small">Keine Abwesenheiten erfasst.</div>
          }
          <div class="dialog-actions">
            <button type="button" (click)="addAbsence()">+ Hinzufügen</button>
            <span class="flex-spacer"></span>
            <button type="button" class="secondary" (click)="absenceEditorOpen.set(false)">Abbrechen</button>
            <button type="button" class="primary" (click)="saveAbsences()" [disabled]="savingAbsences()">
              @if (savingAbsences()) {
                <span class="spinner"></span>
              }
              Speichern
            </button>
          </div>
        </div>
      </div>
    }
  `,
})
export class SprintPage {
  private readonly api = inject(ApiService);
  protected readonly Math = Math;

  readonly team = signal<TeamConfig | null>(null);
  readonly sprintName = signal<string>('');
  readonly dashboard = signal<SprintDashboard | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly aiBusy = signal(false);
  readonly verdicts = signal<SprintVerdict[]>([]);
  readonly absenceEditorOpen = signal(false);
  readonly editAbsences = signal<TeamAbsence[]>([]);
  readonly savingAbsences = signal(false);

  readonly currentAbsences = computed(
    () =>
      this.team()?.sprints.find((s) => s.name === this.sprintName())?.absences ?? [],
  );

  constructor() {
    void this.init();
  }

  private async init(): Promise<void> {
    try {
      const team = await this.api.getTeam();
      this.team.set(team);
      const latest = team?.sprints[team.sprints.length - 1];
      if (latest) {
        this.sprintName.set(latest.name);
        this.editAbsences.set([...latest.absences]);
        await this.load(false);
      }
    } catch (err) {
      this.error.set((err as Error).message);
    }
  }

  selectSprint(name: string): void {
    this.sprintName.set(name);
    this.editAbsences.set([...this.currentAbsences()]);
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

  openAbsenceEditor(): void {
    this.editAbsences.set([...this.currentAbsences()]);
    this.absenceEditorOpen.set(true);
  }

  addAbsence(): void {
    const first = this.team()?.members[0]?.login ?? '';
    const sprint = this.team()?.sprints.find((s) => s.name === this.sprintName());
    const from = sprint?.workdays[0] ?? '';
    this.editAbsences.update((list) => [...list, { login: first, from, to: from }]);
  }

  patchAbsence(index: number, field: keyof TeamAbsence, value: string): void {
    this.editAbsences.update((list) =>
      list.map((a, i) => (i === index ? { ...a, [field]: value } : a)),
    );
  }

  removeAbsence(index: number): void {
    this.editAbsences.update((list) => list.filter((_, i) => i !== index));
  }

  async saveAbsences(): Promise<void> {
    this.savingAbsences.set(true);
    this.error.set(null);
    try {
      const updated = await this.api.saveSprintAbsences(this.sprintName(), this.editAbsences());
      this.team.update((team) =>
        team
          ? {
              ...team,
              sprints: team.sprints.map((s) => (s.name === updated.name ? updated : s)),
            }
          : team,
      );
      this.absenceEditorOpen.set(false);
      await this.load(true);
    } catch (err) {
      this.error.set((err as Error).message);
    } finally {
      this.savingAbsences.set(false);
    }
  }

  shortDay(iso: string): string {
    return formatDayLabel(iso);
  }

  ampel(status: AmpelStatus): string {
    return AMPEL_LABEL[status];
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
