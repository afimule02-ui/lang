import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { TranslateService } from '../../services/translate.service';

@Component({ selector: 'app-transcription', standalone: true, imports: [FormsModule], templateUrl: './transcription.html' })
export class TranscriptionComponent {
  transcript = '';
  constructor(public tr: TranslateService, private router: Router) {}
  next() {
    if (!this.transcript.trim()) return;
    sessionStorage.setItem('eldf_transcript', this.transcript);
    this.router.navigate(['/translation']);
  }
}
