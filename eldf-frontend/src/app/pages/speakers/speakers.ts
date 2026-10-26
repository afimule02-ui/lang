import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { TranslateService } from '../../services/translate.service';
import { ApiService } from '../../services/api.service';
import { Router } from '@angular/router';

@Component({ selector: 'app-speakers', standalone: true, imports: [FormsModule], templateUrl: './speakers.html' })
export class SpeakersComponent {
  speakers = [
    { speakerId: 'SPK-001', ageRange: '', gender: '', educationLevel: '', preferredLanguage: '', region: '', dialect: '' },
    { speakerId: 'SPK-002', ageRange: '', gender: '', educationLevel: '', preferredLanguage: '', region: '', dialect: '' },
  ];
  consent = false;
  error = '';
  loading = false;

  constructor(public tr: TranslateService, private api: ApiService, private router: Router) {}

  async submit() {
    if (!this.consent) { this.error = 'Consent from every participant is required before recording.'; return; }
    const incomplete = this.speakers.find(s => !s.ageRange || !s.educationLevel || !s.preferredLanguage);
    if (incomplete) { this.error = 'Complete each speaker\'s age range, education level, and preferred language.'; return; }
    this.loading = true;
    try {
      await this.api.saveSpeakers(this.speakers);
      this.router.navigate(['/prepare']);
    } catch (e: any) {
      this.error = e?.error?.detail ?? 'Could not save speaker profiles.';
    } finally { this.loading = false; }
  }
}
