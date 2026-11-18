import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslateService } from '../../services/translate.service';
import { ApiService } from '../../services/api.service';
import { CaptureSessionService } from '../../services/capture-session.service';
import { AuthService } from '../../services/auth.service';
import { SPEAKER_CONSENT_COPY, SPEAKER_CONSENT_VERSION } from './speaker-consent-copy';

interface SpeakerDraft {
  speakerId: string;
  name: string;
  ageRange: string;
  gender: string;
  educationLevel: string;
  preferredLanguage: string;
  region: string;
  dialect: string;
  coreConsent: boolean;
  aiDevelopmentConsent: boolean;
  researchSharingConsent: boolean;
}

@Component({ selector: 'app-speakers', standalone: true, imports: [FormsModule], templateUrl: './speakers.html' })
export class SpeakersComponent implements OnInit {
  speakers: SpeakerDraft[] = [];
  speakerCount = 1;
  recordingType = 'Conversation';
  error = '';
  loading = false;
  consentCopy = SPEAKER_CONSENT_COPY;

  constructor(public tr: TranslateService, private api: ApiService, private router: Router,
    private capture: CaptureSessionService, private auth: AuthService) {
    const self = this.auth.user();
    this.speakers = capture.speakers.length ? capture.speakers.map(s => ({
      ...s, coreConsent: false, aiDevelopmentConsent: false, researchSharingConsent: false
    })) : [{
      speakerId: 'SPK-001', name: self?.name ?? '', ageRange: '', gender: '', educationLevel: '',
      preferredLanguage: capture.language(), region: capture.country(), dialect: '', coreConsent: false,
      aiDevelopmentConsent: false, researchSharingConsent: false
    }];
    this.recordingType = capture.recordingType;
    this.syncSpeakers();
  }

  async ngOnInit() {
    await this.auth.ready;
    if (!this.speakers[0].name) this.speakers[0].name = this.auth.user()?.name ?? '';
    try {
      const saved = await this.api.getSpeakers<SpeakerDraft[]>();
      if (saved.length) {
        this.speakers = saved.map(s => ({ ...s, coreConsent: s.coreConsent ?? false,
          aiDevelopmentConsent: s.aiDevelopmentConsent ?? false, researchSharingConsent: s.researchSharingConsent ?? false }));
        this.speakerCount = this.speakers.length;
      }
    } catch { /* New accounts have no speaker profiles yet. */ }
  }

  syncSpeakers() {
    while (this.speakers.length < this.speakerCount) {
      const index = this.speakers.length + 1;
      this.speakers.push({ speakerId: `SPK-${String(index).padStart(3, '0')}`, name: '', ageRange: '', gender: '',
        educationLevel: '', preferredLanguage: this.capture.language(), region: this.capture.country(), dialect: '',
        coreConsent: false, aiDevelopmentConsent: false, researchSharingConsent: false });
    }
    this.speakers = this.speakers.slice(0, this.speakerCount);
  }

  async submit() {
    if (this.loading) return;
    if (this.speakers.some(s => !s.coreConsent)) { this.error = 'Consent from every participant is required before recording.'; return; }
    const incomplete = this.speakers.find(s => !s.name.trim() || !s.ageRange || !s.preferredLanguage);
    if (incomplete) { this.error = 'Complete each speaker\'s name, age range, and preferred language.'; return; }
    this.error = '';
    this.loading = true;
    try {
      await this.api.saveSpeakers(this.speakers.map(s => ({ ...s, consentVersion: SPEAKER_CONSENT_VERSION,
        consentLanguage: this.tr.lang() })));
      this.capture.setSpeakers(this.speakers);
      this.capture.recordingType = this.recordingType;
      this.router.navigate(['/prepare']);
    } catch (e: any) {
      this.error = this.speakerSaveError(e);
    } finally { this.loading = false; }
  }

  private speakerSaveError(error: any): string {
    if (error?.name === 'TimeoutError') return 'Saving speaker profiles took too long. Check your connection and try again. Your form entries are still here.';
    if (error?.status === 0) return 'Could not reach the server. Check your internet connection and try again. Your form entries are still here.';
    if (error?.status >= 500) return 'The server could not save speaker profiles right now. Try again shortly. Your form entries are still here.';
    return error?.error?.detail ?? 'Could not save speaker profiles. Your form entries are still here.';
  }

  async withdraw(speaker: SpeakerDraft) {
    try {
      await this.api.withdrawSpeakerConsent(speaker.speakerId);
      speaker.coreConsent = false;
      speaker.aiDevelopmentConsent = false;
      speaker.researchSharingConsent = false;
      this.capture.setSpeakers(this.speakers);
      this.error = 'Consent withdrawal was recorded. Submit new choices if the speaker wishes to participate again.';
    } catch (e: any) { this.error = e?.error?.detail ?? 'Could not record consent withdrawal.'; }
  }
}
