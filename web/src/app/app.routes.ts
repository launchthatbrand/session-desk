import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { LoginComponent } from './pages/login.component';
import { ShellComponent } from './layout/shell.component';
import { DashboardComponent } from './pages/dashboard.component';
import { BillsComponent } from './pages/bills.component';
import { CalendarComponent } from './pages/calendar.component';
import { AuditComponent } from './pages/audit.component';

export const routes: Routes = [
  { path: 'login', component: LoginComponent },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    children: [
      { path: '', component: DashboardComponent },
      { path: 'bills', component: BillsComponent },
      { path: 'calendar', component: CalendarComponent },
      { path: 'audit', component: AuditComponent },
    ],
  },
  { path: '**', redirectTo: '' },
];
