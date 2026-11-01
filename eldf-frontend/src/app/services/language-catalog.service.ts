import { Injectable } from '@angular/core';
import { Observable, of } from 'rxjs';
import catalogData from '../../../public/data/africa-language-catalog.json';

export interface AfricanLanguage {
  name: string;
  glottocode: string | null;
  iso639_3: string | null;
  level: 'language' | 'dialect' | string;
}

export interface AfricanCountry {
  code: string;
  name: string;
  languages: AfricanLanguage[];
}

export interface AfricanCatalog {
  metadata: { source: string; sourceUrl: string; license: string; retrievedDate: string; notes: string };
  countries: AfricanCountry[];
}

@Injectable({ providedIn: 'root' })
export class LanguageCatalogService {
  private readonly catalogData = catalogData as AfricanCatalog;

  load(): Observable<AfricanCatalog> { return of(this.catalogData); }
}
