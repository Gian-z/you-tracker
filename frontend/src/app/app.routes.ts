import { Routes } from '@angular/router';
import { AssistantPage } from './pages/assistant-page';
import { DashboardPage } from './pages/dashboard-page';
import { SprintPage } from './pages/sprint-page';
import { TasksPage } from './pages/tasks-page';
import { WeekPage } from './pages/week-page';

export const routes: Routes = [
  { path: '', pathMatch: 'full', component: DashboardPage, title: 'you-tracker · Dashboard' },
  { path: 'tasks', component: TasksPage, title: 'you-tracker · Tasks' },
  { path: 'week', component: WeekPage, title: 'you-tracker · Week' },
  { path: 'sprint', component: SprintPage, title: 'you-tracker · Sprint' },
  { path: 'assistant', component: AssistantPage, title: 'you-tracker · Assistant' },
  { path: '**', redirectTo: '' },
];
