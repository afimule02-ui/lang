import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { CaptureSessionService } from '../../services/capture-session.service';
import { AfricanCatalog, AfricanCountry, AfricanLanguage, LanguageCatalogService } from '../../services/language-catalog.service';
import { TranslateService } from '../../services/translate.service';

interface CatalogLanguage extends AfricanLanguage { countries: string[]; }

@Component({
  selector: 'app-catalog',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './catalog.html'
})
export class CatalogComponent implements OnInit {
  countries: AfricanCountry[] = [];
  allLanguages: CatalogLanguage[] = [];
  countryCode = 'all';
  search = '';
  visibleCount = 48;
  loading = true;
  error = '';
  source = '';

  constructor(public tr: TranslateService, private catalog: LanguageCatalogService, private capture: CaptureSessionService) {}

  ngOnInit() {
    this.catalog.load().subscribe({
      next: data => {
        this.countries = [...data.countries].sort((a, b) => a.name.localeCompare(b.name));
        this.source = data.metadata.source;
        const byName = new Map<string, CatalogLanguage>();
        for (const country of data.countries) for (const language of country.languages) {
          const key = language.name.trim().toLocaleLowerCase();
          const existing = byName.get(key);
          if (existing) {
            if (!existing.countries.includes(country.name)) existing.countries.push(country.name);
          } else {
            byName.set(key, { ...language, countries: [country.name] });
          }
        }
        this.allLanguages = [...byName.values()].sort((a, b) => a.name.localeCompare(b.name));
        this.loading = false;
      },
      error: () => { this.error = 'The African language catalog could not be loaded.'; this.loading = false; }
    });
  }

  get selectedCountry() { return this.countries.find(country => country.code === this.countryCode); }

  get filteredLanguages(): CatalogLanguage[] {
    const query = this.search.trim().toLocaleLowerCase();
    return this.allLanguages.filter(language => {
      const inCountry = this.countryCode === 'all' || language.countries.includes(this.selectedCountry?.name ?? '');
      const matches = !query || language.name.toLocaleLowerCase().includes(query) || language.countries.some(name => name.toLocaleLowerCase().includes(query));
      return inCountry && matches;
    });
  }

  get visibleLanguages() {
    const limit = this.countryCode === 'all' ? this.visibleCount : this.filteredLanguages.length;
    return this.filteredLanguages.slice(0, limit);
  }

  get languageCountLabel() {
    return `${this.filteredLanguages.length.toLocaleString()} ${this.tr.t('language names')}`;
  }

  resetPaging() { this.visibleCount = 48; }
  showMore() { this.visibleCount += 48; }

  selectLanguage(language: CatalogLanguage) {
    const country = this.selectedCountry?.name ?? language.countries[0] ?? '';
    this.capture.setCountry(country);
    this.capture.setLanguage(language.name);
  }
}
