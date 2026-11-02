import { Routes } from '@angular/router';
import { authGuard, loginGuard, setupGuard } from './guards/auth.guard';
import { CodecStatisticsPage } from './pages/codec-statistics/codec-statistics.page';
import { CalendarPage } from './pages/calendar/calendar.page';
import { LibraryPage } from './pages/library/library.page';
import { LibraryTrendPage } from './pages/library-trend/library-trend.page';
import { LoginPage } from './pages/login/login.page';
import { LogsPage } from './pages/logs/logs.page';
import { RequestsPage } from './pages/requests/requests.page';
import { SettingsPage } from './pages/settings/settings.page';
import { SetupPage } from './pages/setup/setup.page';
import { pendingChangesGuard } from './guards/pending-changes.guard';

export const appRoutes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'library',
  },
  {
    path: 'login',
    component: LoginPage,
    canActivate: [loginGuard],
  },
  {
    path: 'setup',
    component: SetupPage,
    canActivate: [setupGuard],
  },
  {
    path: 'library',
    component: LibraryPage,
    canActivate: [authGuard],
  },
  {
    path: 'logs',
    component: LogsPage,
    canActivate: [authGuard],
  },
  {
    path: 'settings',
    component: SettingsPage,
    canActivate: [authGuard],
    canDeactivate: [pendingChangesGuard],
  },
  {
    path: 'codec-statistics',
    component: CodecStatisticsPage,
    canActivate: [authGuard],
  },
  {
    path: 'library-trend',
    component: LibraryTrendPage,
    canActivate: [authGuard],
  },
  {
    path: 'requests',
    component: RequestsPage,
    canActivate: [authGuard],
  },
  {
    path: 'calendar',
    component: CalendarPage,
    canActivate: [authGuard],
  },
];
