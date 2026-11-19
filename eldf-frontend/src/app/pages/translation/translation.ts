import { Component, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { TranslateService } from '../../services/translate.service';
import { ApiService } from '../../services/api.service';
import { RouterLink } from '@angular/router';
import { CaptureSessionService } from '../../services/capture-session.service';
import { AuthService } from '../../services/auth.service';

@Component({ selector: 'app-translation', standalone: true, imports: [FormsModule, RouterLink], templateUrl: './translation.html' })
export class TranslationComponent implements OnInit, OnDestroy {
  transcript = '';
  translation = '';
  error = '';
  loading = false;
  success = false;
  private saveTimer?: ReturnType<typeof setTimeout>;

  constructor(
    public tr: TranslateService,
    private api: ApiService,
    public capture: CaptureSessionService,
    private auth: AuthService,
    private route: ActivatedRoute
  ) {}

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
    this.translation = this.capture.translation;
  }

  saveTranslation(value: string) {
    this.translation = value;
    this.capture.translation = value;
    clearTimeout(this.saveTimer);
    this.saveTimer = setTimeout(() => void this.persistTranslation(value), 900);
  }

  private async persistTranslation(value: string) {
    try {
      await this.capture.updateDraft('translation', { transcript: this.transcript, translation: value });
      await this.capture.syncServerDraft(false);
    } catch { this.capture.storageWarning.set('The translation is in this browser but could not be synced to the server yet.'); }
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
      const segments = this.capture.audioSegments.length ? this.capture.audioSegments : [audio];
      if (segments.length > 1) {
        segments.forEach((segment, index) => {
          const extension = segment.type.includes('ogg') ? 'ogg' : segment.type.includes('mp4') ? 'm4a' : 'webm';
          form.append('audio_segments', segment, `recording-part-${index + 1}.${extension}`);
        });
      } else {
        form.append('file', segments[0], this.capture.audioName);
      }
      form.append('transcript', this.transcript);
      form.append('translation', this.translation);
      form.append('language', language);
      form.append('topic_title', this.capture.topicTitle);
      if (this.capture.activeDraft?.serverDraftId) form.append('draft_id', String(this.capture.activeDraft.serverDraftId));
      form.append('speakers_json', JSON.stringify(this.capture.speakers.map(s => s.speakerId).filter(Boolean)));
      const result = await this.api.submitAudio(form);
      if (!result?.ok || !Number.isSafeInteger(result.submissionId)) {
        throw new Error('The server did not confirm that the work was submitted. Your saved draft was kept; retry when the connection is stable.');
      }
      this.success = true;
      try { await this.capture.clear(false); }
      catch { this.capture.storageWarning.set('The work was submitted successfully, but this browser could not clear its local draft.'); }
    } catch (e: any) {
      const status = e?.status;
      this.error = e?.error?.detail
        ?? (status === 0 ? 'The server could not be reached. Your saved draft was kept; check your connection and retry.'
          : status === 413 ? 'The audio upload is larger than the server limit. Your saved draft was kept; contact support before trying again.'
            : status === 504 || status === 408 ? 'The upload took too long or timed out. Your saved draft was kept; retry with a stable connection.'
              : e?.message || 'Submission failed. Your saved draft was kept; retry or contact support.');
    } finally {
      this.loading = false;
    }
  }

  ngOnDestroy() { clearTimeout(this.saveTimer); }
}
