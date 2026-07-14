import { Component, computed, inject, output, signal } from '@angular/core';
import { TicketPicker } from '../components/ticket-picker';
import { formatClock, parseClock, toIsoDate } from '../format';
import {
  AppConfig,
  BookingPreset,
  MeetingRule,
  TaskListItem,
  TeamConfig,
  TeamMember,
  TeamSprint,
  WorkType,
} from '../models';
import { ApiService } from '../services/api.service';
import { SettingsService } from '../services/settings.service';
import { SettingsUiService, SettingsTab } from '../services/settings-ui.service';
import { ThemeService, ThemePreference } from '../services/theme.service';
import { ToastService } from '../services/toast.service';

const TABS: ReadonlyArray<{ key: SettingsTab; label: string }> = [
  { key: 'allg', label: 'Allgemein' },
  { key: 'yt', label: 'YouTrack' },
  { key: 'ki', label: 'KI' },
  { key: 'cal', label: 'Kalender & Git' },
  { key: 'presets', label: 'Presets' },
  { key: 'team', label: 'Team' },
];

const WEEKDAYS: ReadonlyArray<{ full: string; short: string }> = [
  { full: 'Monday', short: 'Mo' },
  { full: 'Tuesday', short: 'Di' },
  { full: 'Wednesday', short: 'Mi' },
  { full: 'Thursday', short: 'Do' },
  { full: 'Friday', short: 'Fr' },
];

/**
 * The ⚙ settings dialog from the mockup: six tabs editing user settings (live),
 * config.json and team.json (saved on «Fertig»), and the booking presets (saved
 * per row). Config/team changes apply without restart — except switching the AI
 * provider (CLI ↔ API key), which the backend reports via requiresRestart.
 */
@Component({
  selector: 'app-settings-dialog',
  imports: [TicketPicker],
  host: { '(document:keydown.escape)': 'onEscape()' },
  template: `
    <div class="overlay" (click)="requestClose()">
      <div class="dialog settings-dialog" (click)="$event.stopPropagation()">
        <div class="dialog-head-row">
          <h2>⚙ Einstellungen</h2>
          <button type="button" class="icon" (click)="requestClose()" aria-label="Schliessen">✕</button>
        </div>

        <div class="settings-tabs">
          @for (t of tabs; track t.key) {
            <button type="button" [class.active]="tab() === t.key" (click)="tab.set(t.key)">
              {{ t.key === 'team' && team() ? 'Team ' + team()!.name : t.label }}
            </button>
          }
        </div>

        @if (error(); as err) {
          <div class="banner error">{{ err }}</div>
        }
        @if (requiresRestart()) {
          <div class="banner info restart-note">
            KI-Provider gewechselt — wirkt erst nach einem Neustart der Anwendung.
          </div>
        }

        <div class="settings-body">
          <!-- ═══ Allgemein ═══ -->
          @if (tab() === 'allg') {
            <div class="settings-section">
              <div class="setting-row toggle">
                <span>
                  <span class="setting-title">Buchungslücke aus Präsenz (Ist)</span>
                  <span class="setting-hint">Lücke = Präsenz − gebucht (alles Anwesende buchen); sonst Fixziel − gebucht</span>
                </span>
                <input
                  type="checkbox"
                  [checked]="settings.usePresence()"
                  (change)="saveSetting({ usePresence: checkboxValue($event) })"
                />
              </div>
              <div class="setting-row">
                <span>
                  <span class="setting-title">Fixes Tagesziel</span>
                  <span class="setting-hint">Format 8:24 — gilt, wenn Präsenz aus ist (leer = aus config.json)</span>
                </span>
                <input
                  class="num"
                  style="text-align:center"
                  [value]="targetValue()"
                  (change)="onTargetChange($event)"
                  placeholder="8:24"
                />
              </div>
              <div class="setting-row">
                <span>
                  <span class="setting-title">Standard-Ticket</span>
                  <span class="setting-hint">Ziel für «Rest des Tages», Lücke & + Buchen</span>
                </span>
                <span class="align-right">
                  <app-ticket-picker
                    [issueId]="settings.defaultIssueId()"
                    [issueSummary]="settings.defaultIssueSummary()"
                    (picked)="onDefaultTicket($event)"
                  />
                </span>
              </div>
              <div class="setting-row">
                <span class="setting-title">Standard-Arbeitstyp</span>
                <select [value]="settings.settings().defaultTypeId ?? ''" (change)="onDefaultType($event)">
                  <option value="">— keiner —</option>
                  @for (t of workTypes(); track t.id) {
                    <option [value]="t.id" [selected]="t.id === settings.settings().defaultTypeId">{{ t.name }}</option>
                  }
                </select>
              </div>
              <div class="setting-row">
                <span>
                  <span class="setting-title">Rundung</span>
                  <span class="setting-hint">Timer- & Schnellbuchungen runden</span>
                </span>
                <select [value]="settings.roundingMinutes()" (change)="onRounding($event)">
                  <option value="0">Keine</option>
                  <option value="5">5 Min</option>
                  <option value="15">15 Min</option>
                </select>
              </div>
              <div class="setting-row toggle">
                <span>
                  <span class="setting-title">KI-Assistent</span>
                  <span class="setting-hint">Assistent-Karte & «Lücken füllen»</span>
                </span>
                <input type="checkbox" [checked]="settings.aiOn()" (change)="settings.setAiOn(checkboxValue($event))" />
              </div>
              <div class="setting-row toggle">
                <span class="setting-title">Timer-Widget</span>
                <input type="checkbox" [checked]="settings.timerOn()" (change)="settings.setTimerOn(checkboxValue($event))" />
              </div>
              <div class="setting-row">
                <span class="setting-title">Design</span>
                <select [value]="theme.preference()" (change)="onTheme($event)">
                  <option value="system">System</option>
                  <option value="light">Hell</option>
                  <option value="dark">Dunkel</option>
                </select>
              </div>
              @if (cfg(); as c) {
                <div class="setting-row">
                  <span>
                    <span class="setting-title">Zeitzone</span>
                    <span class="setting-hint">workday.timezone</span>
                  </span>
                  <input class="num" [value]="c.workday.timezone" (change)="patchWorkday({ timezone: inputValue($event) })" />
                </div>
                <div>
                  <span class="setting-title">«In Arbeit»-Status</span>
                  <span class="setting-hint" style="margin-bottom:0.4rem">Ticket-Status, die als aktiv gelten (workday.inProgressStates)</span>
                  <span class="chip-list">
                    @for (state of c.workday.inProgressStates; track $index) {
                      <span class="chip">{{ state }}<button type="button" (click)="removeInProgress($index)">✕</button></span>
                    }
                    <input placeholder="+ Status, Enter" (keydown.enter)="addInProgress($event)" />
                  </span>
                </div>
              } @else {
                <div class="loading"><span class="spinner"></span> Lade Konfiguration…</div>
              }
            </div>
          }

          <!-- ═══ YouTrack ═══ -->
          @if (tab() === 'yt' && cfg(); as c) {
            <div class="settings-section">
              <div class="setting-row">
                <span class="setting-title">Server-URL</span>
                <input class="num" [value]="c.youTrack.baseUrl" (change)="patchYouTrack({ baseUrl: inputValue($event) })" />
              </div>
              <div class="setting-row">
                <span class="setting-title">Web-URL</span>
                <input class="num" [value]="c.youTrack.webBaseUrl" (change)="patchYouTrack({ webBaseUrl: inputValue($event) })" />
              </div>
              <div class="setting-row">
                <span class="setting-title">Token</span>
                <span style="display:flex; gap:0.4rem">
                  <input
                    style="flex:1"
                    class="num"
                    [type]="showToken() ? 'text' : 'password'"
                    [value]="c.youTrack.token"
                    (change)="patchYouTrack({ token: inputValue($event) })"
                  />
                  <button type="button" class="icon" (click)="showToken.set(!showToken())" title="Anzeigen / verbergen">👁</button>
                </span>
              </div>
              <div style="display:flex; justify-content:flex-end; gap:0.4rem; align-items:center">
                @if (testResult(); as msg) {
                  <span class="small" [class.muted]="false">{{ msg }}</span>
                }
                <button type="button" (click)="testYouTrack()" [disabled]="testing()">Verbindung testen</button>
              </div>
              <div>
                <span class="setting-hint">Meine Tickets (issueQuery — $dev wird ersetzt)</span>
                <input class="wide-input" [value]="c.youTrack.issueQuery ?? ''" (change)="patchYouTrack({ issueQuery: inputValueOrNull($event) })" />
              </div>
              <div>
                <span class="setting-hint">Sprint-Pool (sprintPoolQuery)</span>
                <input class="wide-input" [value]="c.youTrack.sprintPoolQuery ?? ''" (change)="patchYouTrack({ sprintPoolQuery: inputValueOrNull($event) })" />
              </div>
              <div>
                <span class="setting-hint">Ganzer Sprint (sprintQuery)</span>
                <input class="wide-input" [value]="c.youTrack.sprintQuery ?? ''" (change)="patchYouTrack({ sprintQuery: inputValueOrNull($event) })" />
              </div>
            </div>
          }

          <!-- ═══ KI ═══ -->
          @if (tab() === 'ki' && cfg(); as c) {
            <div class="settings-section">
              <div class="setting-row">
                <span class="setting-title">API-Key</span>
                <span style="display:flex; gap:0.4rem">
                  <input
                    style="flex:1"
                    class="num"
                    [type]="showKey() ? 'text' : 'password'"
                    [value]="c.anthropic.apiKey"
                    (change)="patchAnthropic({ apiKey: inputValue($event) })"
                  />
                  <button type="button" class="icon" (click)="showKey.set(!showKey())" title="Anzeigen / verbergen">👁</button>
                </span>
              </div>
              <div class="setting-row">
                <span>
                  <span class="setting-title">Modell</span>
                  <span class="setting-hint">z.B. claude-opus-4-8, claude-sonnet-5</span>
                </span>
                <input class="num" [value]="c.anthropic.model" (change)="patchAnthropic({ model: inputValue($event) })" list="ki-models" />
                <datalist id="ki-models">
                  <option value="claude-opus-4-8"></option>
                  <option value="claude-sonnet-5"></option>
                  <option value="claude-haiku-4-5"></option>
                </datalist>
              </div>
              <div class="setting-row">
                <span>
                  <span class="setting-title">CLI-Befehl</span>
                  <span class="setting-hint">Ohne API-Key: Claude Code CLI (headless)</span>
                </span>
                <input class="num" [value]="c.anthropic.cliCommand" (change)="patchAnthropic({ cliCommand: inputValue($event) })" />
              </div>
              <div style="display:flex; justify-content:flex-end; gap:0.4rem; align-items:center">
                @if (testResult(); as msg) {
                  <span class="small">{{ msg }}</span>
                }
                <button type="button" (click)="testAi()" [disabled]="testing()">Prüfen</button>
              </div>
              <div class="banner info">
                Der Assistent schlägt nur vor — Buchungen erreichen YouTrack erst nach deiner Bestätigung im
                «Entwürfe prüfen»-Dialog. Ein Wechsel zwischen CLI und API-Key braucht einen Neustart.
              </div>
            </div>
          }

          <!-- ═══ Kalender & Git ═══ -->
          @if (tab() === 'cal' && cfg(); as c) {
            <div class="settings-section">
              <div class="setting-row">
                <span class="setting-title">ICS-URL</span>
                <span style="display:flex; gap:0.4rem">
                  <input style="flex:1" class="num" [value]="c.calendar?.icsUrl ?? ''" (change)="patchIcsUrl($event)" />
                  <button type="button" (click)="testCalendar()" [disabled]="testing()">Testen</button>
                </span>
              </div>
              @if (testResult(); as msg) {
                <div class="small" style="text-align:right">{{ msg }}</div>
              }
              <div>
                <span class="setting-title" style="margin-bottom:0.4rem">Termin-Regeln → Ticket</span>
                @for (rule of c.calendar?.rules ?? []; track $index) {
                  <div class="rule-row">
                    <input class="num" [value]="rule.pattern" (change)="patchRule($index, { pattern: inputValue($event) })" />
                    <span class="arrow">→</span>
                    <input class="num" [value]="rule.issueId" (change)="patchRule($index, { issueId: inputValue($event) })" />
                    <button type="button" class="icon" (click)="removeRule($index)">✕</button>
                  </div>
                }
                <button type="button" class="ghost-add" (click)="addRule()">+ Regel</button>
              </div>
              <div>
                <span class="setting-title" style="margin-bottom:0.4rem">Git — Scan-Verzeichnisse</span>
                @for (root of c.git?.scanRoots ?? []; track $index) {
                  <div style="display:flex; gap:0.4rem; align-items:center; margin-bottom:0.35rem">
                    <span class="num" style="flex:1; font-size:var(--fs-s); color:var(--muted)">{{ root }}</span>
                    <button type="button" class="icon" (click)="removeGitRoot($index)">✕</button>
                  </div>
                }
                <input
                  class="wide-input"
                  placeholder="+ Pfad, Enter (z.B. C:/repos)"
                  (keydown.enter)="addGitRoot($event)"
                />
              </div>
            </div>
          }

          <!-- ═══ Presets ═══ -->
          @if (tab() === 'presets') {
            <div class="settings-section">
              <span class="setting-hint">Erscheinen als Schnellbuchungs-Chips auf «Heute». Name · Ticket · Dauer · Kommentar</span>
              @for (p of presets(); track p.id) {
                <div class="preset-editor-row">
                  <input [value]="p.name" (change)="savePresetField(p, { name: inputValue($event) })" />
                  <input class="num" [value]="p.issueId" (change)="savePresetField(p, { issueId: inputValue($event) })" />
                  <input
                    class="num"
                    style="text-align:center"
                    [value]="formatClock(p.minutes)"
                    (change)="onPresetMinutes(p, $event)"
                  />
                  <input [value]="p.comment ?? ''" placeholder="Kommentar" (change)="savePresetField(p, { comment: inputValueOrNull($event) })" />
                  <button type="button" class="icon" (click)="deletePreset(p)">✕</button>
                </div>
              }
              <button type="button" class="ghost-add" (click)="addPreset()">+ Preset</button>
            </div>
          }

          <!-- ═══ Team ═══ -->
          @if (tab() === 'team') {
            @if (team(); as t) {
              <div class="settings-section">
                <div class="chip-list">
                  <span class="setting-title" style="margin-right:0.25rem">Projekte</span>
                  @for (p of t.projects; track $index) {
                    <span class="chip num">{{ p }}<button type="button" (click)="removeProject($index)">✕</button></span>
                  }
                  <input placeholder="+ Projekt" (keydown.enter)="addProject($event)" />
                </div>
                <div>
                  <span class="setting-hint">Task-Query</span>
                  <input class="wide-input" [value]="t.taskQuery" (change)="patchTeam({ taskQuery: inputValue($event) })" />
                </div>
                <div>
                  <span class="setting-hint">Feature-Query</span>
                  <input class="wide-input" [value]="t.featureSprintQuery" (change)="patchTeam({ featureSprintQuery: inputValue($event) })" />
                </div>
                <div class="chip-list">
                  <span class="setting-title" style="margin-right:0.25rem">Zeremonien</span>
                  @for (cp of t.ceremonyPatterns; track $index) {
                    <span class="chip">{{ cp }}<button type="button" (click)="removeCeremony($index)">✕</button></span>
                  }
                  <input placeholder="+ Pattern" (keydown.enter)="addCeremony($event)" />
                </div>
                <div>
                  <span class="setting-title" style="margin-bottom:0.4rem">Mitglieder</span>
                  @for (m of t.members; track m.login) {
                    <div class="member-row">
                      <span class="num" style="font-weight:600; color:var(--accent-strong); font-size:var(--fs-xs)">{{ m.login }}</span>
                      <span style="font-size:var(--fs-s); overflow:hidden; text-overflow:ellipsis; white-space:nowrap">
                        {{ m.name }}{{ m.login.toLowerCase() === currentLogin().toLowerCase() ? ' (du)' : '' }}
                      </span>
                      <input
                        class="num"
                        style="text-align:center; font-size:var(--fs-xs)"
                        title="Tages-Soll"
                        [value]="formatClock(m.thresholdMinutes)"
                        (change)="onMemberSoll(m, $event)"
                      />
                      <span class="weekday-toggles">
                        @for (wd of weekdays; track wd.full) {
                          <button
                            type="button"
                            [class.on]="m.weekdays.includes(wd.full)"
                            (click)="toggleMemberDay(m, wd.full)"
                          >
                            {{ wd.short }}
                          </button>
                        }
                      </span>
                    </div>
                  }
                </div>
                <div>
                  <span class="setting-title" style="margin-bottom:0.4rem">Sprints & Absenzen</span>
                  @for (sp of sortedSprints(); track sp.name) {
                    <div class="sprint-block" [class.active]="sp.name === t.activeSprint">
                      <div class="sprint-block-head">
                        <button
                          type="button"
                          class="sprint-radio"
                          [class.on]="sp.name === t.activeSprint"
                          (click)="patchTeam({ activeSprint: sp.name })"
                          title="Als aktiven Sprint wählen"
                        ></button>
                        <span class="num" style="font-weight:600">{{ sp.name }}</span>
                        <span class="small muted">{{ sp.workdays.length }} Arbeitstage · {{ sprintRange(sp) }}</span>
                        @if (sp.name === t.activeSprint) {
                          <span class="small" style="margin-left:auto; color:var(--accent-strong); font-weight:600">aktiv</span>
                        }
                      </div>
                      @for (a of sp.absences; track $index) {
                        <div class="absence-grid-row">
                          <select [value]="a.login" (change)="patchAbsence(sp, $index, { login: inputValue($event) })">
                            @for (m of t.members; track m.login) {
                              <option [value]="m.login" [selected]="m.login === a.login">{{ m.login }}</option>
                            }
                          </select>
                          <input type="date" [value]="a.from" (change)="patchAbsence(sp, $index, { from: inputValue($event) })" />
                          <input type="date" [value]="a.to" (change)="patchAbsence(sp, $index, { to: inputValue($event) })" />
                          <button type="button" class="icon" (click)="removeAbsence(sp, $index)">✕</button>
                        </div>
                      }
                      <button type="button" class="ghost-add" style="margin-top:0.5rem" (click)="addAbsence(sp)">+ Absenz</button>
                    </div>
                  }
                  <div style="display:flex; gap:0.4rem; align-items:center; flex-wrap:wrap; margin-top:0.5rem">
                    <input placeholder="Sprint-Name" [value]="newSprintName()" (input)="newSprintName.set(inputValue($event))" style="width:8rem" />
                    <input type="date" [value]="newSprintFrom()" (input)="newSprintFrom.set(inputValue($event))" />
                    <input type="date" [value]="newSprintTo()" (input)="newSprintTo.set(inputValue($event))" />
                    <button type="button" class="ghost-add" (click)="addSprint()">+ Sprint (Mo–Fr)</button>
                  </div>
                </div>
              </div>
            } @else {
              <div class="banner info">
                Keine team.json gefunden — die Team-Konfiguration wird beim ersten Sprint-Setup angelegt.
              </div>
            }
          }
        </div>

        <div class="settings-footer">
          <span class="settings-path">%APPDATA%/you-tracker — wird beim Speichern geschrieben</span>
          <button type="button" class="primary" (click)="finish()" [disabled]="saving()">
            {{ saving() ? 'Speichern…' : 'Fertig' }}
          </button>
        </div>
      </div>
    </div>
  `,
})
export class SettingsDialog {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  protected readonly settings = inject(SettingsService);
  protected readonly theme = inject(ThemeService);
  private readonly settingsUi = inject(SettingsUiService);

  readonly closed = output<void>();

  protected readonly tabs = TABS;
  protected readonly weekdays = WEEKDAYS;
  protected readonly formatClock = formatClock;

  protected readonly tab = signal<SettingsTab>(this.settingsUi.tab());
  protected readonly cfg = signal<AppConfig | null>(null);
  protected readonly team = signal<TeamConfig | null>(null);
  protected readonly presets = signal<BookingPreset[]>([]);
  protected readonly workTypes = signal<WorkType[]>([]);
  protected readonly currentLogin = signal('');

  protected readonly showToken = signal(false);
  protected readonly showKey = signal(false);
  protected readonly testing = signal(false);
  protected readonly testResult = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly requiresRestart = signal(false);

  protected readonly newSprintName = signal('');
  protected readonly newSprintFrom = signal('');
  protected readonly newSprintTo = signal('');

  private configDirty = false;
  private teamDirty = false;

  protected readonly targetValue = computed(() => {
    const target = this.settings.settings().targetMinutes;
    return target === null ? '' : formatClock(target);
  });

  protected readonly sortedSprints = computed(() => {
    const t = this.team();
    return t ? [...t.sprints].sort((a, b) => (a.workdays[0] < b.workdays[0] ? 1 : -1)) : [];
  });

  constructor() {
    void this.api.getConfig().then((c) => this.cfg.set(c)).catch((err: Error) => this.error.set(err.message));
    void this.api.getTeam().then((t) => this.team.set(t)).catch(() => undefined);
    void this.api.getPresets().then((p) => this.presets.set(p)).catch(() => undefined);
    void this.api.getWorkTypes().then((t) => this.workTypes.set(t)).catch(() => undefined);
    void this.api.getMeta().then((m) => this.currentLogin.set(m.currentUser.login)).catch(() => undefined);
  }

  // --- template helpers ---

  protected inputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected inputValueOrNull(event: Event): string | null {
    const value = this.inputValue(event).trim();
    return value.length > 0 ? value : null;
  }

  protected checkboxValue(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  // --- Allgemein ---

  protected saveSetting(changes: Parameters<SettingsService['save']>[0]): void {
    void this.settings.save(changes).catch((err: Error) => this.error.set(err.message));
  }

  protected onTargetChange(event: Event): void {
    const raw = this.inputValue(event).trim();
    if (raw.length === 0) {
      this.saveSetting({ targetMinutes: null });
      return;
    }
    const minutes = parseClock(raw);
    if (minutes !== null && minutes >= 60) {
      this.saveSetting({ targetMinutes: minutes });
    }
  }

  protected onDefaultTicket(item: TaskListItem): void {
    this.saveSetting({ defaultIssueId: item.issueId, defaultIssueSummary: item.summary });
  }

  protected onDefaultType(event: Event): void {
    const id = (event.target as HTMLSelectElement).value || null;
    const name = this.workTypes().find((t) => t.id === id)?.name ?? null;
    this.saveSetting({ defaultTypeId: id, defaultTypeName: name });
  }

  protected onRounding(event: Event): void {
    this.saveSetting({ roundingMinutes: parseInt((event.target as HTMLSelectElement).value, 10) || 0 });
  }

  protected onTheme(event: Event): void {
    this.theme.preference.set((event.target as HTMLSelectElement).value as ThemePreference);
  }

  // --- config patches (saved on Fertig) ---

  private patchCfg(mutate: (c: AppConfig) => AppConfig): void {
    const current = this.cfg();
    if (!current) {
      return;
    }
    this.cfg.set(mutate(structuredClone(current)));
    this.configDirty = true;
  }

  protected patchYouTrack(changes: Partial<AppConfig['youTrack']>): void {
    this.patchCfg((c) => ({ ...c, youTrack: { ...c.youTrack, ...changes } }));
  }

  protected patchAnthropic(changes: Partial<AppConfig['anthropic']>): void {
    this.patchCfg((c) => ({ ...c, anthropic: { ...c.anthropic, ...changes } }));
  }

  protected patchWorkday(changes: Partial<AppConfig['workday']>): void {
    this.patchCfg((c) => ({ ...c, workday: { ...c.workday, ...changes } }));
  }

  protected removeInProgress(index: number): void {
    this.patchCfg((c) => {
      c.workday.inProgressStates.splice(index, 1);
      return c;
    });
  }

  protected addInProgress(event: Event): void {
    event.preventDefault();
    const input = event.target as HTMLInputElement;
    const value = input.value.trim();
    if (value) {
      this.patchWorkday({ inProgressStates: [...(this.cfg()?.workday.inProgressStates ?? []), value] });
      input.value = '';
    }
  }

  protected patchIcsUrl(event: Event): void {
    const url = this.inputValueOrNull(event);
    this.patchCfg((c) => ({ ...c, calendar: { icsUrl: url, rules: c.calendar?.rules ?? [] } }));
  }

  protected patchRule(index: number, changes: Partial<MeetingRule>): void {
    this.patchCfg((c) => {
      const rules = [...(c.calendar?.rules ?? [])];
      rules[index] = { ...rules[index], ...changes };
      return { ...c, calendar: { icsUrl: c.calendar?.icsUrl ?? null, rules } };
    });
  }

  protected removeRule(index: number): void {
    this.patchCfg((c) => {
      const rules = [...(c.calendar?.rules ?? [])];
      rules.splice(index, 1);
      return { ...c, calendar: { icsUrl: c.calendar?.icsUrl ?? null, rules } };
    });
  }

  protected addRule(): void {
    this.patchCfg((c) => ({
      ...c,
      calendar: {
        icsUrl: c.calendar?.icsUrl ?? null,
        rules: [
          ...(c.calendar?.rules ?? []),
          { pattern: '*Muster*', issueId: this.settings.defaultIssueId() ?? '', workTypeName: null, comment: null },
        ],
      },
    }));
  }

  protected removeGitRoot(index: number): void {
    this.patchCfg((c) => {
      const roots = [...(c.git?.scanRoots ?? [])];
      roots.splice(index, 1);
      return { ...c, git: { scanRoots: roots, author: c.git?.author ?? null } };
    });
  }

  protected addGitRoot(event: Event): void {
    event.preventDefault();
    const input = event.target as HTMLInputElement;
    const value = input.value.trim();
    if (value) {
      this.patchCfg((c) => ({
        ...c,
        git: { scanRoots: [...(c.git?.scanRoots ?? []), value], author: c.git?.author ?? null },
      }));
      input.value = '';
    }
  }

  // --- connection tests (probe the current, possibly unsaved values) ---

  protected testYouTrack(): void {
    const c = this.cfg();
    if (c) {
      void this.runTest(() => this.api.testYouTrack(c.youTrack.baseUrl, c.youTrack.token));
    }
  }

  protected testAi(): void {
    const c = this.cfg();
    if (c) {
      void this.runTest(() => this.api.testAi(c.anthropic.apiKey, c.anthropic.model, c.anthropic.cliCommand));
    }
  }

  protected testCalendar(): void {
    const url = this.cfg()?.calendar?.icsUrl;
    if (url) {
      void this.runTest(() => this.api.testCalendar(url));
    }
  }

  private async runTest(work: () => Promise<{ message: string }>): Promise<void> {
    this.testing.set(true);
    this.testResult.set(null);
    try {
      this.testResult.set((await work()).message);
    } catch (err) {
      this.testResult.set(`✗ ${(err as Error).message}`);
    } finally {
      this.testing.set(false);
    }
  }

  // --- presets (saved per row) ---

  protected savePresetField(preset: BookingPreset, changes: Partial<BookingPreset>): void {
    void this.api
      .savePreset({ ...preset, ...changes })
      .then((saved) => this.presets.update((list) => list.map((p) => (p.id === saved.id ? saved : p))))
      .catch((err: Error) => this.error.set(err.message));
  }

  protected onPresetMinutes(preset: BookingPreset, event: Event): void {
    const minutes = parseClock(this.inputValue(event));
    if (minutes !== null && minutes > 0) {
      this.savePresetField(preset, { minutes });
    }
  }

  protected deletePreset(preset: BookingPreset): void {
    void this.api
      .deletePreset(preset.id)
      .then(() => this.presets.update((list) => list.filter((p) => p.id !== preset.id)))
      .catch((err: Error) => this.error.set(err.message));
  }

  protected addPreset(): void {
    const draft = {
      id: null,
      name: 'Neues Preset',
      issueId: this.settings.defaultIssueId() ?? '',
      issueSummary: this.settings.defaultIssueSummary() ?? '',
      minutes: 30,
      typeId: this.settings.defaultTypeId(),
      typeName: this.settings.settings().defaultTypeName,
      comment: null,
    };
    if (!draft.issueId) {
      this.error.set('Zuerst ein Standard-Ticket wählen (Allgemein) oder das Preset-Ticket direkt eintragen.');
      return;
    }
    void this.api
      .savePreset(draft)
      .then((saved) => this.presets.update((list) => [...list, saved]))
      .catch((err: Error) => this.error.set(err.message));
  }

  // --- team (saved on Fertig) ---

  protected patchTeam(changes: Partial<TeamConfig>): void {
    const current = this.team();
    if (current) {
      this.team.set({ ...structuredClone(current), ...changes });
      this.teamDirty = true;
    }
  }

  protected removeProject(index: number): void {
    const t = this.team();
    if (t) {
      this.patchTeam({ projects: t.projects.filter((_, i) => i !== index) });
    }
  }

  protected addProject(event: Event): void {
    event.preventDefault();
    const input = event.target as HTMLInputElement;
    const value = input.value.trim().toUpperCase();
    const t = this.team();
    if (value && t) {
      this.patchTeam({ projects: [...t.projects, value] });
      input.value = '';
    }
  }

  protected removeCeremony(index: number): void {
    const t = this.team();
    if (t) {
      this.patchTeam({ ceremonyPatterns: t.ceremonyPatterns.filter((_, i) => i !== index) });
    }
  }

  protected addCeremony(event: Event): void {
    event.preventDefault();
    const input = event.target as HTMLInputElement;
    const value = input.value.trim();
    const t = this.team();
    if (value && t) {
      this.patchTeam({ ceremonyPatterns: [...t.ceremonyPatterns, value] });
      input.value = '';
    }
  }

  protected onMemberSoll(member: TeamMember, event: Event): void {
    const minutes = parseClock(this.inputValue(event));
    if (minutes !== null && minutes > 0) {
      this.patchMember(member.login, { thresholdMinutes: minutes });
    }
  }

  protected toggleMemberDay(member: TeamMember, day: string): void {
    const weekdays = member.weekdays.includes(day)
      ? member.weekdays.filter((d) => d !== day)
      : [...member.weekdays, day];
    this.patchMember(member.login, { weekdays });
  }

  private patchMember(login: string, changes: Partial<TeamMember>): void {
    const t = this.team();
    if (t) {
      this.patchTeam({ members: t.members.map((m) => (m.login === login ? { ...m, ...changes } : m)) });
    }
  }

  protected sprintRange(sprint: TeamSprint): string {
    const fmt = (iso: string) => `${iso.slice(8, 10)}.${iso.slice(5, 7)}.`;
    return sprint.workdays.length > 0
      ? `${fmt(sprint.workdays[0])} – ${fmt(sprint.workdays[sprint.workdays.length - 1])}`
      : '—';
  }

  private patchSprint(name: string, mutate: (sp: TeamSprint) => TeamSprint): void {
    const t = this.team();
    if (t) {
      this.patchTeam({ sprints: t.sprints.map((sp) => (sp.name === name ? mutate(structuredClone(sp)) : sp)) });
    }
  }

  protected patchAbsence(sprint: TeamSprint, index: number, changes: Partial<TeamSprint['absences'][number]>): void {
    this.patchSprint(sprint.name, (sp) => {
      sp.absences[index] = { ...sp.absences[index], ...changes };
      return sp;
    });
  }

  protected removeAbsence(sprint: TeamSprint, index: number): void {
    this.patchSprint(sprint.name, (sp) => {
      sp.absences.splice(index, 1);
      return sp;
    });
  }

  protected addAbsence(sprint: TeamSprint): void {
    const login = this.currentLogin() || this.team()?.members[0]?.login || '';
    const first = sprint.workdays[0] ?? toIsoDate(new Date());
    this.patchSprint(sprint.name, (sp) => {
      sp.absences.push({ login, from: first, to: first });
      return sp;
    });
  }

  protected addSprint(): void {
    const t = this.team();
    const name = this.newSprintName().trim();
    if (!t || !name || !this.newSprintFrom() || !this.newSprintTo()) {
      return;
    }
    // Workdays = Mo–Fr within the range, matching the backend's AddSprintCommand.
    const workdays: string[] = [];
    for (
      let d = new Date(`${this.newSprintFrom()}T12:00:00`);
      toIsoDate(d) <= this.newSprintTo();
      d.setDate(d.getDate() + 1)
    ) {
      if (d.getDay() !== 0 && d.getDay() !== 6) {
        workdays.push(toIsoDate(d));
      }
    }
    if (workdays.length === 0) {
      this.error.set('Der Zeitraum enthält keine Werktage.');
      return;
    }
    this.patchTeam({ sprints: [...t.sprints, { name, workdays, absences: [] }] });
    this.newSprintName.set('');
    this.newSprintFrom.set('');
    this.newSprintTo.set('');
  }

  // --- finish / close ---

  protected onEscape(): void {
    this.requestClose();
  }

  protected requestClose(): void {
    // Escape/backdrop = same path as «Fertig»: pending config/team edits are saved.
    void this.finish();
  }

  protected async finish(): Promise<void> {
    if (this.saving()) {
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    try {
      if (this.configDirty && this.cfg()) {
        const result = await this.api.saveConfig(this.cfg()!);
        this.cfg.set(result.config);
        this.configDirty = false;
        if (result.requiresRestart) {
          this.requiresRestart.set(true);
        }
      }
      if (this.teamDirty && this.team()) {
        this.team.set(await this.api.saveTeam(this.team()!));
        this.teamDirty = false;
      }
      if (this.requiresRestart()) {
        // keep the dialog open so the restart note is seen; next click closes
        this.toast.show('Einstellungen gespeichert');
        this.requiresRestart.set(false);
        return;
      }
      this.closed.emit();
    } catch (err) {
      this.error.set((err as Error).message);
    } finally {
      this.saving.set(false);
    }
  }
}
