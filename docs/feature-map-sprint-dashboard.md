# Feature Map — Sprint-Zeitbuchungen Dashboard (Scrum Master view)

Source: skill `youtrack-sprint-zeitbuchungen` + `team-config-st6` (2026-07). Goal: integrate the
skill's per-developer sprint dashboard into you-tracker as a first-class page, replacing the
manual Bash/curl/Node flow with the app's ports, caching, and AI layer.

## 1. Feature inventory → mapping

| # | Skill feature | Exists in app today | New work |
|---|---|---|---|
| F1 | Team config: logins, thresholds (420 min/d), weekday availability, per-sprint absences, `{SPRINT}` query templates, hidden users | Personal config only (`config.json`), user directory | **Team config** `team.json` (%APPDATA%) + `ITeamConfigStore` port in Storage module |
| F2 | Work items per dev over sprint period | ✅ `IWorkItemReader.GetWorkItemsAsync(devLogin, from, to)` — author filtering & ms-date gotchas already solved | Loop over team logins (fan-out in handler, cached per dev) |
| F3 | Roadmapvorhaben (RMV) mapping: task → parent feature via `Subtask INWARD` link → `Roadmapvorhaben` custom field | ❌ | New port method on a **`ISprintReader`**: `GetSprintTaskCategoriesAsync(sprint)` — issues query with `links(...)` fields |
| F4 | Sprint features with Estimation / Spent / RMV / Assignee | Custom-field mapper covers Estimation/Spent | `ISprintReader.GetSprintFeaturesAsync(sprint)` using config `featureSprintQuery` |
| F5 | RMV categories: Strategisch/Optimierung/Kundenprojekt = Roadmap; Support/Admin/DevOps = non-roadmap | ❌ | Constant map in Core (company-wide standard), overridable in team config |
| F6 | S1 Heatmap: devs × sprint days, total h, threshold colors, absence/today states | `DaySummary` per single dev | **`SprintMetricsCalculator`** (pure): `BuildHeatmap(...)`; availability model from F1 |
| F7 | S2 Roadmap-Gap: per dev, roadmap-hours vs threshold × available days, % attainment | Personal gap exists (total, not RMV-classified) | `BuildRoadmapGaps(...)` (pure) on F2×F3×F5 + F1 availability |
| F8 | S3 Feature-Schätzabweichungen: top-15 abs gap, ceremonies excluded | `HygieneKind.OverEstimate` (single issue) | `BuildFeatureDeviations(...)` (pure) on F4; ceremony exclusion list in team config |
| F9 | S4 Fazit: Ampel (✅/⚠/🔴/⬜) + 4 KPIs + 2-paragraph text per dev | AI layer (Claude CLI/API hybrid), confirm-before-write culture | **Ampel = deterministic rules** in `SprintMetricsCalculator` (skill criteria verbatim); **prose = AI query** fed with computed KPIs as fixed facts (AI never judges, only verbalizes) |
| F10 | Gotchas (ms dates, `work author` over-matching, `$top`, `/dev/stdin`) | ✅ all handled by `YouTrackClient` / irrelevant in-process | Only new one: link traversal (F3) |
| F11 | Dark dashboard, sticky first column, mono numbers | App design system, light+dark | New **Sprint page** in Angular; adapt skill's dark-only colors to both themes (status colors + text, dataviz rules) |

Explicitly **not** ported (skill marks as rejected): stacked-bar RMV distribution, sprint vs
non-sprint breakdown.

## 2. Architecture placement (CQRS)

```
Core
├── Abstractions: ISprintReader (new port) · ITeamConfigStore (new port)
│                 records: SprintTaskCategory, SprintFeature, TeamMember, TeamConfig, SprintPeriod
├── Metrics:      SprintMetricsCalculator (pure): BuildHeatmap · BuildRoadmapGaps ·
│                 BuildFeatureDeviations · Ampel(dev) — unit-test target #1
├── Application:  GetSprintDashboardQuery(SprintPeriod) : IQuery<SprintDashboard> (cacheable)
│                 GenerateSprintVerdictsQuery(SprintPeriod) : IQuery<SprintVerdicts> (AI, prose only)
Infrastructure.YouTrack   → implements ISprintReader (fields=…links…, $top guards)
Infrastructure.Storage    → implements ITeamConfigStore (team.json; ST6 seeded from team-config-st6.md)
Web                       → GET /api/sprint/dashboard?from&to · POST /api/ai/sprint-verdicts
frontend                  → new route /sprint: S1 heatmap, S2 gap bars, S3 deviations, S4 verdict cards
```

Reused unchanged: dispatcher + TTL caching, error envelope, IAiProvider hybrid, DurationFormat,
user directory (resolve full names for logins).

## 3. Deterministic vs AI split (S4)

| Piece | Where | Why |
|---|---|---|
| KPIs (days booked, roadmap %, estimation adherence, unknown-hours) | `SprintMetricsCalculator` | Numbers must be reproducible |
| Ampel classification (skill's ✅/⚠/🔴/⬜ criteria incl. "viel buchen ≠ viel liefern") | `SprintMetricsCalculator` | Rules are exact thresholds — no LLM variance |
| 2-paragraph Fazit text (German) | AI query via `IAiProvider` | Wording; receives KPIs+Ampel as immutable facts, references feature IDs |

## 4. Team config shape (team.json draft)

```json
{
  "name": "ST6",
  "scrumTeam": "Scrum Team 6",
  "projects": ["ST6", "XBOX"],
  "taskQuery": "Sprint: {SPRINT} AND ScrumTeam: {Scrum Team 6} AND Typ: Task",
  "featureSprintQuery": "Sprint: {SPRINT} AND ScrumTeam: {Scrum Team 6} AND Typ: Feature",
  "ceremonyPatterns": ["Daily", "Planning", "Retro", "Review", "Refinement"],
  "members": [
    { "login": "GZW", "thresholdMinutes": 420, "weekdays": ["Mo","Di","Mi","Do","Fr"] },
    { "login": "BEM", "thresholdMinutes": 420, "weekdays": ["Di","Mi","Do","Fr"] }
  ],
  "hiddenLogins": ["..."],
  "sprints": [
    {
      "name": "2026.06-2",
      "workdays": ["2026-06-18", "2026-06-19", "…"],
      "absences": [ { "login": "MHE", "from": "2026-06-22", "to": "2026-07-01" } ]
    }
  ]
}
```

`{SPRINT}` placeholder substituted from the selected sprint name (same pattern as `$dev`).

## 5. Phasing

| Phase | Content | Verify |
|---|---|---|
| **P1 data** | ITeamConfigStore + team.json (ST6 seeded) · ISprintReader (tasks+links → RMV, features) in YouTrack module | fixture tests + read-only live probe of both queries |
| **P2 metrics** | SprintMetricsCalculator: heatmap, roadmap gaps, deviations, Ampel — pure + fully unit-tested against the Sprint 2026.06-2 observations from team-config-st6 (BEM 42% 🔴, PCL 85% ✅ …) as golden cases | `dotnet test` |
| **P3 UI** | /api/sprint/dashboard · Angular /sprint page S1+S2 (dataviz pass: heatmap = status colors + numeric cell text, both themes) | headless screenshot |
| **P4 verdicts** | S3 table + S4 Ampel cards + AI prose query | live gap: one verdict generation vs the golden table |

## 6. Open questions

1. **Sprint selection**: page date-picker + sprint name field, or maintain the sprint list (name,
   workdays, absences) in team.json and pick from a dropdown? (Draft assumes team.json list.)
2. **Absence upkeep**: stays manual in team.json (as in the skill's md config) — acceptable?
3. **Verdict language**: German (matches the skill's Fazit template + team) — confirm.
4. **Visibility**: the Sprint page shows the whole team's data to whoever runs the app — fine for
   a personal tool, but say if it should be tucked behind a nav toggle.
