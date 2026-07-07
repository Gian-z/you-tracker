import { Routes } from '@angular/router';
import { DashboardPage } from './pages/dashboard-page';
import { SprintPage } from './pages/sprint-page';
import { TasksPage } from './pages/tasks-page';
import { WeekPage } from './pages/week-page';

export const routes: Routes = [
  { path: '', pathMatch: 'full', component: DashboardPage, title: 'you-tracker · Heute' },
  { path: 'tickets', component: TasksPage, title: 'you-tracker · Tickets' },
  { path: 'woche', component: WeekPage, title: 'you-tracker · Woche' },
  { path: 'sprint', component: SprintPage, title: 'you-tracker · Sprint' },
  // Old routes stay as redirects so bookmarks survive; the assistant folded into Heute.
  { path: 'tasks', redirectTo: 'tickets' },
  { path: 'week', redirectTo: 'woche' },
  { path: 'assistant', redirectTo: '' },
  { path: '**', redirectTo: '' },
];
