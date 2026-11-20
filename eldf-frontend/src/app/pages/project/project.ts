import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AfricanCountry, AfricanLanguage, LanguageCatalogService } from '../../services/language-catalog.service';
import { TranslateService } from '../../services/translate.service';
import { CaptureSessionService } from '../../services/capture-session.service';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-project',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
  <div class="page-heading"><div><div class="eyebrow">{{ tr.t('AFRICA · PROJECT SETUP') }}</div>
  <h1>{{ tr.t('Choose a project') }}</h1><p>{{ tr.t('Choose a country and the language community you are collecting with.') }}</p></div></div>
  @if (loading) { <section class="workspace-card">{{ tr.t('Loading') }}…</section> }
  @else if (error) { <section class="workspace-card form-error">{{ tr.t(error) }}</section> }
  @else {
    <section class="workspace-card">
      <div class="form-grid">
        <label class="field"><span>{{ tr.t('Country or territory') }} *</span>
          <select [(ngModel)]="countryCode" name="country" (ngModelChange)="countryChanged()">
            @for (country of countries; track country.code) { <option [value]="country.code">{{ country.name }}</option> }
          </select>
        </label>
        <label class="field"><span>{{ tr.t('Find a language') }}</span>
          <input [(ngModel)]="search" name="languageSearch" [placeholder]="tr.t('Type a language name')" />
        </label>
      </div>
      <div class="form-grid">
        <label class="field"><span>{{ tr.t('Language') }} *</span>
          <select [(ngModel)]="selectedLanguage" name="language" required>
            <option value="">{{ tr.t('Select a language') }}</option>
            @for (language of filteredLanguages; track language.iso639_3 || language.name) {
              <option [value]="language.name">{{ language.name }}{{ language.level === 'dialect' ? ' · ' + tr.t('Dialect') : '' }}</option>
            }
          </select>
          <small>{{ filteredLanguages.length.toLocaleString() }} {{ tr.t('languages available for this country') }}</small>
        </label>
        @if (selectedCountry) { <div class="field"><span>{{ tr.t('Country or territory') }}</span><p>{{ selectedCountry.name }}</p></div> }
      </div>
      <div class="form-footer">
        <button type="button" class="primary-button" [disabled]="!selectedLanguage || starting" (click)="continue()">
          {{ starting ? tr.t('Saving started work…') : tr.t('Continue') }}
        </button>
      </div>
    </section>
  }`
})
export class ProjectComponent implements OnInit {
  countries: AfricanCountry[] = [];
  countryCode = 'ET';
  selectedLanguage = '';
  search = '';
  loading = true;
  starting = false;
  error = '';

  constructor(public tr: TranslateService, private catalog: LanguageCatalogService, private capture: CaptureSessionService, private auth: AuthService, private router: Router) {}

  ngOnInit() {
    this.catalog.load().subscribe({
      next: data => {
        this.countries = [...data.countries].sort((a, b) => a.name.localeCompare(b.name));
        const rememberedCountry = this.countries.find(country => country.name === this.capture.country());
        if (rememberedCountry) this.countryCode = rememberedCountry.code;
        const previousLanguage = this.capture.language();
        if (previousLanguage) this.selectedLanguage = this.selectedCountry?.languages.find(language => language.name === previousLanguage)?.name ?? '';
        if (!this.selectedLanguage) this.selectedLanguage = this.selectedCountry?.languages.find(language => language.iso639_3 === 'amh')?.name ?? '';
        this.loading = false;
      },
      error: () => { this.error = 'The African language catalog could not be loaded.'; this.loading = false; }
    });
  }

  get selectedCountry() { return this.countries.find(country => country.code === this.countryCode); }

  get filteredLanguages(): AfricanLanguage[] {
    const query = this.search.trim().toLocaleLowerCase();
    return (this.selectedCountry?.languages ?? []).filter(language => !query || language.name.toLocaleLowerCase().includes(query));
  }

  countryChanged() {
    this.selectedLanguage = '';
    this.search = '';
  }

  async continue() {
    if (!this.selectedLanguage || !this.selectedCountry || this.starting) return;
    this.starting = true;
    this.error = '';
    try {
      await this.auth.ready;
      const user = this.auth.user();
      if (user) {
        // A new project must not silently resume the previous recording. Preserve that
        // unfinished work as a server draft before freeing the browser's single local slot.
        if (!this.capture.activeDraft) await this.capture.restoreDraft(user.id);
        await this.capture.parkCurrentDraft();
      }
    } catch {
      this.error = 'Your started work could not be saved safely. It is still in this browser; try again when the connection is stable.';
      this.starting = false;
      return;
    }
    this.capture.setCountry(this.selectedCountry.name);
    this.capture.setLanguage(this.selectedLanguage);
    await this.router.navigate(['/speakers']);
    this.starting = false;
  }
}
