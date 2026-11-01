import { Routes } from '@angular/router';
import { roleGuard } from './services/role.guard';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./pages/home/home').then(m => m.HomeComponent) },
  { path: 'login/:role', loadComponent: () => import('./pages/login/login').then(m => m.LoginComponent) },
  { path: 'forgot-password', loadComponent: () => import('./pages/forgot-password/forgot-password').then(m => m.ForgotPasswordComponent) },
  { path: 'change-password', loadComponent: () => import('./pages/change-password/change-password').then(m => m.ChangePasswordComponent) },
  { path: 'register', loadComponent: () => import('./pages/register/register').then(m => m.RegisterComponent) },
  { path: 'dashboard', canActivate: [roleGuard], data: { roles: ['contributor'] }, loadComponent: () => import('./pages/dashboard/dashboard').then(m => m.DashboardComponent) },
  { path: 'catalog', loadComponent: () => import('./pages/catalog/catalog').then(m => m.CatalogComponent) },
  { path: 'project', canActivate: [roleGuard], data: { roles: ['contributor'] }, loadComponent: () => import('./pages/project/project').then(m => m.ProjectComponent) },
  { path: 'speakers', canActivate: [roleGuard], data: { roles: ['contributor'] }, loadComponent: () => import('./pages/speakers/speakers').then(m => m.SpeakersComponent) },
  { path: 'prepare', canActivate: [roleGuard], data: { roles: ['contributor'] }, loadComponent: () => import('./pages/prepare/prepare').then(m => m.PrepareComponent) },
  { path: 'record', canActivate: [roleGuard], data: { roles: ['contributor'] }, loadComponent: () => import('./pages/record/record').then(m => m.RecordComponent) },
  { path: 'transcription', canActivate: [roleGuard], data: { roles: ['contributor'] }, loadComponent: () => import('./pages/transcription/transcription').then(m => m.TranscriptionComponent) },
  { path: 'translation', canActivate: [roleGuard], data: { roles: ['contributor'] }, loadComponent: () => import('./pages/translation/translation').then(m => m.TranslationComponent) },
  { path: 'admin', canActivate: [roleGuard], data: { roles: ['admin'] }, loadComponent: () => import('./pages/admin/admin').then(m => m.AdminComponent) },
  { path: 'review', canActivate: [roleGuard], data: { roles: ['admin', 'agent'] }, loadComponent: () => import('./pages/review/review').then(m => m.ReviewComponent) },
  { path: 'agent', canActivate: [roleGuard], data: { roles: ['agent'] }, loadComponent: () => import('./pages/agent/agent').then(m => m.AgentComponent) },
  { path: 'change-password', canActivate: [roleGuard], loadComponent: () => import('./pages/change-password/change-password').then(m => m.ChangePasswordComponent) },
  { path: '**', redirectTo: '' }
];
