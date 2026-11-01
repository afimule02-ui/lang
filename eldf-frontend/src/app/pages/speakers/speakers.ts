import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslateService } from '../../services/translate.service';
import { ApiService } from '../../services/api.service';
import { CaptureSessionService } from '../../services/capture-session.service';
import { AuthService } from '../../services/auth.service';

interface SpeakerDraft {
  speakerId: string;
  name: string;
  ageRange: string;
  gender: string;
  educationLevel: string;
  preferredLanguage: string;
  region: string;
  dialect: string;
}

@Component({ selector: 'app-speakers', standalone: true, imports: [FormsModule], templateUrl: './speakers.html' })
export class SpeakersComponent implements OnInit {
  speakers: SpeakerDraft[] = [];
  speakerCount = 1;
  recordingType = 'Conversation';
  consent = false;
  error = '';
  loading = false;

  constructor(public tr: TranslateService, private api: ApiService, private router: Router,
    private capture: CaptureSessionService, private auth: AuthService) {
    const self = this.auth.user();
    this.speakers = [{
      speakerId: 'SPK-001', name: self?.name ?? '', ageRange: '', gender: '', educationLevel: '',
      preferredLanguage: capture.language(), region: capture.country(), dialect: ''
    }];
    this.recordingType = capture.recordingType;
    this.syncSpeakers();
  }

  async ngOnInit() {
    await this.auth.ready;
    if (!this.speakers[0].name) this.speakers[0].name = this.auth.user()?.name ?? '';
  }

  syncSpeakers() {
    while (this.speakers.length < this.speakerCount) {
      const index = this.speakers.length + 1;
      this.speakers.push({ speakerId: `SPK-${String(index).padStart(3, '0')}`, name: '', ageRange: '', gender: '',
        educationLevel: '', preferredLanguage: this.capture.language(), region: this.capture.country(), dialect: '' });
    }
    this.speakers = this.speakers.slice(0, this.speakerCount);
  }

  async submit() {
    if (!this.consent) { this.error = 'Consent from every participant is required before recording.'; return; }
    const incomplete = this.speakers.find(s => !s.name.trim() || !s.ageRange || !s.educationLevel || !s.preferredLanguage);
    if (incomplete) { this.error = 'Complete each speaker\'s name, age range, education level, and preferred language.'; return; }
    this.loading = true;
    try {
      await this.api.saveSpeakers(this.speakers);
      this.capture.setSpeakers(this.speakers);
      this.capture.recordingType = this.recordingType;
      this.router.navigate(['/prepare']);
    } catch (e: any) {
      this.error = e?.error?.detail ?? 'Could not save speaker profiles.';
    } finally { this.loading = false; }
  }
}
