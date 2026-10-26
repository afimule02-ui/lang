import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./pages/home/home').then(m => m.HomeComponent) },
  { path: 'login/:role', loadComponent: () => import('./pages/login/login').then(m => m.LoginComponent) },
  { path: 'register', loadComponent: () => import('./pages/register/register').then(m => m.RegisterComponent) },
  { path: 'dashboard', loadComponent: () => import('./pages/dashboard/dashboard').then(m => m.DashboardComponent) },
  { path: 'catalog', loadComponent: () => import('./pages/catalog/catalog').then(m => m.CatalogComponent) },
  { path: 'project', loadComponent: () => import('./pages/project/project').then(m => m.ProjectComponent) },
  { path: 'speakers', loadComponent: () => import('./pages/speakers/speakers').then(m => m.SpeakersComponent) },
  { path: 'prepare', loadComponent: () => import('./pages/prepare/prepare').then(m => m.PrepareComponent) },
  { path: 'record', loadComponent: () => import('./pages/record/record').then(m => m.RecordComponent) },
  { path: 'transcription', loadComponent: () => import('./pages/transcription/transcription').then(m => m.TranscriptionComponent) },
  { path: 'translation', loadComponent: () => import('./pages/translation/translation').then(m => m.TranslationComponent) },
  { path: 'admin', loadComponent: () => import('./pages/admin/admin').then(m => m.AdminComponent) },
  { path: 'review', loadComponent: () => import('./pages/review/review').then(m => m.ReviewComponent) },
  { path: 'agent', loadComponent: () => import('./pages/agent/agent').then(m => m.AgentComponent) },
  { path: '**', redirectTo: '' }
];
