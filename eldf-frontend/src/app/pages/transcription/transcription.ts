import { Component, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { TranslateService } from '../../services/translate.service';
import { CaptureSessionService } from '../../services/capture-session.service';
import { AuthService } from '../../services/auth.service';
import { ApiService } from '../../services/api.service';

@Component({ selector: 'app-transcription', standalone: true, imports: [FormsModule], templateUrl: './transcription.html' })
export class TranscriptionComponent implements OnInit, OnDestroy {
  transcript = '';
  audioUrl = '';
  audioUrls: string[] = [];
  private saveTimer?: ReturnType<typeof setTimeout>;
  constructor(public tr: TranslateService, public capture: CaptureSessionService, private router: Router, private route: ActivatedRoute, private auth: AuthService, private api: ApiService) {
    this.transcript = capture.transcript;
  }

  async ngOnInit() {
    await this.auth.ready;
    const user = this.auth.user();
    if (user && (!this.capture.activeDraft || !this.capture.audio)) {
      try {
        const serverDraftId = Number(this.route.snapshot.queryParamMap.get('draftId'));
        const localDraft = await this.capture.restoreDraft(user.id);
        if (Number.isSafeInteger(serverDraftId) && serverDraftId > 0 && localDraft?.serverDraftId !== serverDraftId) {
          const serverDraft = await this.api.getDraft<any>(serverDraftId);
          const audio = serverDraft.hasAudio ? await this.api.getDraftAudio(serverDraftId) : null;
          await this.capture.restoreServerDraft(user.id, serverDraft, audio);
        }
      } catch { this.capture.storageWarning.set('The saved work could not be opened from browser storage or the server.'); }
    }
    this.transcript = this.capture.transcript;
    this.audioUrls = this.capture.audioSegments.map(segment => URL.createObjectURL(segment));
    this.audioUrl = this.audioUrls[0] ?? (this.capture.audio ? URL.createObjectURL(this.capture.audio) : '');
  }

  saveTranscript(value: string) {
    this.transcript = value;
    this.capture.transcript = value;
    clearTimeout(this.saveTimer);
    this.saveTimer = setTimeout(() => void this.persistTranscript(value), 900);
  }

  private async persistTranscript(value: string) {
    try {
      await this.capture.updateDraft('transcription', { transcript: value });
      await this.capture.syncServerDraft(false);
    } catch { this.capture.storageWarning.set('The transcript is in this browser but could not be synced to the server yet.'); }
  }

  async next() {
    if (!this.transcript.trim()) return;
    this.capture.transcript = this.transcript;
    clearTimeout(this.saveTimer);
    try {
      await this.capture.updateDraft('translation', { transcript: this.transcript });
      await this.capture.syncServerDraft(false);
    } catch { this.capture.storageWarning.set('Your transcript is saved in this browser but could not be synced to the server yet.'); }
    this.router.navigate(['/translation'], { queryParams: this.capture.activeDraft?.serverDraftId ? { draftId: this.capture.activeDraft.serverDraftId } : {} });
  }

  ngOnDestroy() {
    clearTimeout(this.saveTimer);
    for (const url of this.audioUrls) URL.revokeObjectURL(url);
    if (this.audioUrl && !this.audioUrls.includes(this.audioUrl)) URL.revokeObjectURL(this.audioUrl);
  }
}
