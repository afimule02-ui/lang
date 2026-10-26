import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslateService } from '../../services/translate.service';

@Component({ selector: 'app-project', standalone: true, imports: [RouterLink],
  template: `
  <div class="page-heading"><div><div class="eyebrow">YOUR WORKSPACE</div>
  <h1>{{ tr.t('Choose a project') }}</h1></div></div>
  <div class="project-grid">
    @for (p of projects; track p.id) {
      <button class="project-card" [class.selected]="selected===p.id" (click)="selected=p.id">
        <span class="language-symbol">{{ p.name[0] }}</span>
        <strong>{{ tr.t(p.name) }}</strong>
        <small>{{ p.id }}</small>
        <span class="progress-track"><i [style.width.%]="p.progress"></i></span>
      </button>
    }
  </div>
  @if (selected) {
    <div class="form-footer"><a routerLink="/speakers" class="primary-button">{{ tr.t('Continue') }}</a></div>
  }` })
export class ProjectComponent {
  selected = '';
  projects = [
    { id: 'PRJ-100', name: 'Amharic', progress: 56 },
    { id: 'PRJ-101', name: 'Afaan Oromo', progress: 32 },
    { id: 'PRJ-102', name: 'Tigrinya', progress: 44 },
    { id: 'PRJ-103', name: 'Somali', progress: 28 },
    { id: 'PRJ-104', name: 'Afar', progress: 15 },
    { id: 'PRJ-105', name: 'Sidama', progress: 22 },
    { id: 'PRJ-106', name: 'Wolaytta', progress: 18 },
  ];
  constructor(public tr: TranslateService) {}
}
