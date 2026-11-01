import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { TranslateService } from '../../services/translate.service';
import { CaptureSessionService } from '../../services/capture-session.service';

@Component({ selector: 'app-transcription', standalone: true, imports: [FormsModule], templateUrl: './transcription.html' })
export class TranscriptionComponent {
  transcript = '';
  constructor(public tr: TranslateService, private capture: CaptureSessionService, private router: Router) {
    this.transcript = capture.transcript;
  }
  next() {
    if (!this.transcript.trim()) return;
    this.capture.transcript = this.transcript;
    this.router.navigate(['/translation']);
  }
}
