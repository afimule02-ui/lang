import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslateService } from '../../services/translate.service';

@Component({
  selector: 'app-catalog',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './catalog.html'
})
export class CatalogComponent {
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
