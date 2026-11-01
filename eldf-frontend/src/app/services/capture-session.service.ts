import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class CaptureSessionService {
  private readonly languageKey = 'eldf_project_language';
  private readonly countryKey = 'eldf_project_country';
  language = signal(sessionStorage.getItem(this.languageKey) ?? '');
  country = signal(sessionStorage.getItem(this.countryKey) ?? '');
  audio: Blob | null = null;
  transcript = '';
  speakers: any[] = [];
  recordingType = 'Conversation';
  topics: string[] = [];

  setLanguage(language: string) {
    this.language.set(language);
    sessionStorage.setItem(this.languageKey, language);
  }

  setCountry(country: string) {
    this.country.set(country);
    sessionStorage.setItem(this.countryKey, country);
  }

  setSpeakers(speakers: any[]) { this.speakers = speakers.map(speaker => ({ ...speaker })); }
  setTopics(topics: string[]) { this.topics = [...topics]; }

  clear() {
    this.audio = null;
    this.transcript = '';
    this.speakers = [];
    this.recordingType = 'Conversation';
    this.topics = [];
  }
}
