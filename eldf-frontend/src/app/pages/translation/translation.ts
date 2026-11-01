import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslateService } from '../../services/translate.service';
import { ApiService } from '../../services/api.service';
import { RouterLink } from '@angular/router';
import { CaptureSessionService } from '../../services/capture-session.service';

@Component({ selector: 'app-translation', standalone: true, imports: [FormsModule, RouterLink], templateUrl: './translation.html' })
export class TranslationComponent implements OnInit {
  transcript = '';
  translation = '';
  error = '';
  loading = false;
  success = false;

  constructor(
    public tr: TranslateService,
    private api: ApiService,
    private capture: CaptureSessionService
  ) {}

  ngOnInit() {
    this.transcript = this.capture.transcript;
  }

  async submit() {
    if (!this.transcript.trim() || !this.translation.trim()) {
      this.error = 'Add both the transcript and its translation before submitting.';
      return;
    }
    this.loading = true;
    this.error = '';
    try {
      const audio = this.capture.audio;
      if (!audio) { this.error = 'No audio found. Please record audio first.'; return; }
      const language = this.capture.language();
      if (!language) { this.error = 'Choose a language project before submitting.'; return; }
      const form = new FormData();
      form.append('file', audio, audio.type === 'audio/wav' ? 'recording.wav' : 'recording.webm');
      form.append('transcript', this.transcript);
      form.append('translation', this.translation);
      form.append('language', language);
      await this.api.submitAudio(form);
      this.success = true;
      this.capture.clear();
    } catch (e: any) {
      this.error = e?.error?.detail ?? 'Submission failed.';
    } finally {
      this.loading = false;
    }
  }
}
