import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslateService } from '../../services/translate.service';
import { ApiService } from '../../services/api.service';
import { AuthService } from '../../services/auth.service';

@Component({ selector: 'app-translation', standalone: true, imports: [FormsModule], templateUrl: './translation.html' })
export class TranslationComponent implements OnInit {
  transcript = '';
  translation = '';
  notes = '';
  error = '';
  loading = false;
  success = false;

  constructor(
    public tr: TranslateService,
    private api: ApiService,
    private auth: AuthService,
    private router: Router
  ) {}

  ngOnInit() {
    this.transcript = sessionStorage.getItem('eldf_transcript') ?? '';
  }

  async submit() {
    if (!this.transcript.trim() || !this.translation.trim()) {
      this.error = 'Add both the transcript and its translation before submitting.';
      return;
    }
    this.loading = true;
    this.error = '';
    try {
      const audioData = sessionStorage.getItem('eldf_audio_blob');
      if (!audioData) { this.error = 'No audio found. Please record audio first.'; return; }
      const form = new FormData();
      const blob = await fetch(audioData).then(r => r.blob());
      form.append('file', blob, 'recording.webm');
      form.append('transcript', this.transcript);
      form.append('translation', this.translation);
      form.append('language', 'Amharic');
      await this.api.submitAudio(form);
      this.success = true;
      sessionStorage.removeItem('eldf_transcript');
      sessionStorage.removeItem('eldf_audio_blob');
    } catch (e: any) {
      this.error = e?.error?.detail ?? 'Submission failed.';
    } finally {
      this.loading = false;
    }
  }
}
