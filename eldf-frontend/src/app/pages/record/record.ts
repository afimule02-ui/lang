import { ChangeDetectorRef, Component, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslateService } from '../../services/translate.service';
import { CaptureSessionService } from '../../services/capture-session.service';
import { AuthService } from '../../services/auth.service';
import { ApiService } from '../../services/api.service';

type RecordState = 'testReady' | 'testing' | 'testReview' | 'idle' | 'recording' | 'paused' | 'done';
interface SignalSample { time: number; rms: number; peak: number; }

@Component({ selector: 'app-record', standalone: true, imports: [], templateUrl: './record.html' })
export class RecordComponent implements OnInit, OnDestroy {
  state: RecordState = 'testReady';
  elapsed = 0;
  testElapsed = 0;
  notice = '';
  testFeedback: string[] = [];
  microphoneLevel = 0;
  testAudioUrl = '';
  testAudioType = 'audio/webm';
  audioBlob: Blob | null = null;
  audioUrl = '';
  audioSegmentUrls: string[] = [];
  private recorder: MediaRecorder | null = null;
  private chunks: Blob[] = [];
  private clock: ReturnType<typeof setInterval> | undefined;
  private startedAt = 0;
  private elapsedMs = 0;
  private activeStream: MediaStream | null = null;
  private audioContext: AudioContext | null = null;
  private analyser: AnalyserNode | null = null;
  private waveform = new Uint8Array(0);
  private signalSamples: SignalSample[] = [];
  private destroyed = false;
  private fileSelection = 0;
  private persistQueue: Promise<void> = Promise.resolve();
  private lastServerAudioSyncSeconds = 0;
  recoveredDraft = false;
  testStarting = false;

  constructor(public tr: TranslateService, public capture: CaptureSessionService, private router: Router,
    private route: ActivatedRoute, private changeDetector: ChangeDetectorRef, private auth: AuthService, private api: ApiService) {}

  async ngOnInit() {
    await this.auth.ready;
    const user = this.auth.user();
    if (!user) return;
    try {
      const serverDraftId = Number(this.route.snapshot.queryParamMap.get('draftId'));
      let draft;
      if (Number.isSafeInteger(serverDraftId) && serverDraftId > 0) {
        const localDraft = await this.capture.restoreDraft(user.id);
        if (localDraft?.serverDraftId === serverDraftId) draft = localDraft;
        else {
          const serverDraft = await this.api.getDraft<any>(serverDraftId);
          const audio = serverDraft.hasAudio ? await this.api.getDraftAudio(serverDraftId) : null;
          draft = await this.capture.restoreServerDraft(user.id, serverDraft, audio);
        }
      } else draft = await this.capture.restoreDraft(user.id);
      if (draft?.stage === 'transcription' && this.capture.audio) {
        await this.router.navigate(['/transcription'], { queryParams: draft.serverDraftId ? { draftId: draft.serverDraftId } : {} });
        return;
      }
      if (draft?.stage === 'translation' && this.capture.audio) {
        await this.router.navigate(['/translation'], { queryParams: draft.serverDraftId ? { draftId: draft.serverDraftId } : {} });
        return;
      }
      if (draft?.stage === 'recording' && this.capture.audio) {
        this.audioBlob = this.capture.audio;
        this.refreshAudioUrls();
        this.capture.audioName = this.fileNameFor(this.audioBlob.type);
        this.elapsed = draft.elapsedSeconds;
        this.elapsedMs = draft.elapsedSeconds * 1000;
        this.state = 'done';
        this.recoveredDraft = true;
        this.notice = 'An interrupted recording was recovered from this browser. Listen to it, then continue recording or proceed when you are ready.';
      } else if (draft?.stage === 'recording') {
        this.state = 'idle';
        this.notice = 'A started recording was found, but no saved audio parts are available to recover yet. Keep this browser storage intact and start recording again.';
      }
      this.changeDetector.detectChanges();
    } catch {
      this.capture.storageWarning.set('The saved work could not be opened. Keep this page open and do not clear browser data.');
    }
  }

  get clock_display() { return this.formatTime(this.elapsed); }
  get test_clock_display() { return this.formatTime(this.testElapsed); }

  async startTest() {
    if (this.testStarting || this.state !== 'testReady') return;
    this.testStarting = true;
    this.notice = '';
    if (!this.hasSpeakerConsent()) {
      this.notice = 'Current consent from each selected speaker is required before recording.';
      this.testStarting = false;
      return;
    }
    this.testFeedback = [];
    this.testElapsed = 0;
    this.signalSamples = [];
    const availability = this.microphoneAvailabilityError();
    if (availability) {
      this.notice = availability;
      this.testStarting = false;
      return;
    }
    try {
      await this.beginCapture(true);
    } catch (e: any) {
      this.cleanupMedia();
      this.state = 'testReady';
      this.notice = this.microphoneError(e);
    } finally {
      this.testStarting = false;
      this.changeDetector.detectChanges();
    }
  }

  private async beginCapture(isTest: boolean) {
    // Start both browser operations directly from the user's click. iOS Safari can
    // suspend AudioContext if resume() is first called after getUserMedia resolves.
    const streamRequest = navigator.mediaDevices.getUserMedia({ audio: true });
    const AudioContextClass = window.AudioContext || (window as any).webkitAudioContext;
    if (!AudioContextClass) throw new DOMException('Audio analysis is not supported.', 'OperationError');
    this.audioContext = new AudioContextClass();
    const resumeRequest = this.audioContext.resume();
    const [streamResult, contextResult] = await Promise.allSettled([streamRequest, resumeRequest]);
    if (streamResult.status === 'rejected') throw streamResult.reason;
    const stream = streamResult.value;
    this.activeStream = stream;
    const audioTracks = stream.getAudioTracks();
    if (!stream.active || !audioTracks.length || audioTracks.some(track => track.readyState !== 'live')) {
      throw new DOMException('No live microphone audio track was acquired.', 'NotReadableError');
    }
    if (contextResult.status === 'rejected' || this.audioContext.state !== 'running') {
      throw new DOMException('Audio analysis did not start.', 'OperationError');
    }
    this.analyser = this.audioContext.createAnalyser();
    this.analyser.fftSize = 1024;
    this.audioContext.createMediaStreamSource(stream).connect(this.analyser);
    this.waveform = new Uint8Array(this.analyser.fftSize);
    this.chunks = [];
    if (!isTest && !this.capture.activeDraft) {
      try {
        const user = this.auth.user();
        if (user) await this.capture.beginLocalDraft(user.id, 'recording');
      } catch {
        this.capture.storageWarning.set('Browser storage is unavailable. The recording may be lost if this page closes.');
      }
    }
    if (!isTest && this.capture.activeDraft && !this.capture.activeDraft.serverDraftId) {
      try { await this.capture.syncServerDraft(false); }
      catch { this.capture.storageWarning.set('This recording is saved on this browser, but could not yet be listed for the admin team.'); }
    }
    const mimeType = typeof MediaRecorder.isTypeSupported === 'function'
      ? ['audio/webm;codecs=opus', 'audio/webm', 'audio/ogg;codecs=opus', 'audio/mp4']
        .find(type => MediaRecorder.isTypeSupported(type))
      : undefined;
    this.recorder = new MediaRecorder(stream, mimeType ? { mimeType } : undefined);
    if (!isTest) this.lastServerAudioSyncSeconds = this.elapsed;
    this.recorder.ondataavailable = event => {
      if (!event.data.size) return;
      this.chunks.push(event.data);
      if (!isTest) {
        const elapsed = Math.max(this.elapsed, Math.floor((this.elapsedMs + (this.state === 'recording' ? Date.now() - this.startedAt : 0)) / 1000));
        this.persistQueue = this.persistQueue.then(async () => {
          await this.capture.appendRecordingChunk(event.data, elapsed);
          if (elapsed - this.lastServerAudioSyncSeconds >= 60) {
            this.lastServerAudioSyncSeconds = elapsed;
            try { await this.capture.syncServerDraft(true); }
            catch { this.capture.storageWarning.set('Your audio is saved in this browser. The server copy could not be updated yet; keep this browser data and continue recording.'); }
          }
        })
          .catch(() => this.capture.storageWarning.set('The recording could not be fully saved to browser storage. Keep this page open and download a copy before leaving.'));
      }
    };
    this.recorder.onstop = async () => {
      const blob = new Blob(this.chunks, { type: this.recorder?.mimeType || 'audio/webm' });
      this.cleanupMedia();
      if (this.destroyed) return;
      if (isTest) {
        if (this.testAudioUrl) URL.revokeObjectURL(this.testAudioUrl);
        this.testAudioUrl = URL.createObjectURL(blob);
        this.testAudioType = blob.type || 'audio/webm';
        this.reviewTest();
      } else {
        await this.persistQueue;
        const user = this.auth.user();
        if (user) {
          try { await this.capture.restoreDraft(user.id); }
          catch { this.capture.storageWarning.set('The recording is still available in this page but the saved copy could not be refreshed. Download a copy before leaving.'); }
        }
        this.audioBlob = this.capture.audio ?? blob;
        this.capture.audio = this.audioBlob;
        this.capture.audioName = this.fileNameFor(this.audioBlob.type);
        this.refreshAudioUrls();
        this.state = 'done';
        this.recoveredDraft = false;
        await this.capture.updateDraft('recording', { elapsedSeconds: this.elapsed });
        try { await this.capture.syncServerDraft(true); }
        catch { this.capture.storageWarning.set('Your recording is safe in this browser, but the server copy could not be updated. Keep this browser data and retry before submitting.'); }
      }
      this.changeDetector.detectChanges();
    };
    this.recorder.start(1000);
    if (this.recorder.state !== 'recording') throw new DOMException('Audio recorder did not start.', 'OperationError');
    this.startedAt = Date.now();
    this.state = isTest ? 'testing' : 'recording';
    this.clock = setInterval(() => {
      const seconds = Math.floor((Date.now() - this.startedAt) / 1000);
      if (isTest) {
        this.testElapsed = seconds;
        this.captureSignalSample(seconds);
        if (seconds >= 30) this.stopTest();
      } else {
        this.elapsed = Math.floor((this.elapsedMs + Date.now() - this.startedAt) / 1000);
        this.updateMicrophoneLevel();
        if (this.elapsed >= 3600) this.stop();
      }
      this.changeDetector.detectChanges();
    }, 100);
  }

  private captureSignalSample(time: number) {
    if (!this.analyser) return;
    this.analyser.getByteTimeDomainData(this.waveform);
    let sum = 0;
    let peak = 0;
    for (const value of this.waveform) {
      const normalized = (value - 128) / 128;
      const absolute = Math.abs(normalized);
      sum += normalized * normalized;
      peak = Math.max(peak, absolute);
    }
    this.signalSamples.push({ time, rms: Math.sqrt(sum / this.waveform.length), peak });
    this.microphoneLevel = Math.min(100, Math.round(Math.sqrt(sum / this.waveform.length) * 400));
  }

  private microphoneAvailabilityError(): string {
    if (!window.isSecureContext) return 'Microphone access requires HTTPS. Open the secure site address or use localhost for local testing.';
    if (!navigator.mediaDevices || typeof navigator.mediaDevices.getUserMedia !== 'function') return 'This browser does not provide microphone access. Use a current version of Chrome or Edge.';
    if (typeof MediaRecorder === 'undefined') return 'This browser can access a microphone but cannot record audio. Use a current version of Chrome or Edge.';
    if (typeof window.AudioContext === 'undefined') return 'This browser cannot analyze the microphone test. Use a current version of Chrome or Edge.';
    return '';
  }

  private updateMicrophoneLevel() {
    if (!this.analyser || !this.waveform.length) { this.microphoneLevel = 0; return; }
    this.analyser.getByteTimeDomainData(this.waveform);
    let sum = 0;
    for (const value of this.waveform) {
      const normalized = (value - 128) / 128;
      sum += normalized * normalized;
    }
    this.microphoneLevel = Math.min(100, Math.round(Math.sqrt(sum / this.waveform.length) * 400));
  }

  private microphoneError(error: any): string {
    switch (error?.name) {
      case 'NotAllowedError': case 'PermissionDeniedError': return 'Microphone permission was denied. Allow microphone access in your browser settings, then try again.';
      case 'SecurityError': return 'The browser blocked microphone access for security reasons. Open the site over HTTPS and check browser microphone permissions.';
      case 'NotFoundError': case 'DevicesNotFoundError': return 'No microphone was found. Connect a microphone and try again.';
      case 'NotReadableError': case 'TrackStartError': return 'The microphone is busy or could not be opened. Close other apps using it and try again.';
      case 'OperationError': return 'The microphone opened, but audio capture could not start. Close other apps using audio and try again.';
      case 'OverconstrainedError': return 'The selected microphone settings are unavailable. Check your audio device and try again.';
      case 'AbortError': return 'Microphone access was interrupted. Check the device and try again.';
      default: return error?.message || 'The microphone could not be opened. Check browser permission and device settings.';
    }
  }

  private reviewTest() {
    const ambient = this.signalSamples.filter(sample => sample.time < 3);
    const speech = this.signalSamples.filter(sample => sample.time >= 3);
    const avg = (samples: SignalSample[]) => samples.length
      ? samples.reduce((total, sample) => total + sample.rms, 0) / samples.length : 0;
    const ambientRms = avg(ambient);
    const speechRms = avg(speech);
    const clipping = speech.filter(sample => sample.peak >= 0.98).length / Math.max(1, speech.length);
    const silenceThreshold = Math.max(0.008, ambientRms * 2.2);
    let quietSince: number | null = null;
    const interruptions: number[] = [];
    for (const sample of speech) {
      if (sample.rms < silenceThreshold) quietSince ??= sample.time;
      else if (quietSince !== null) {
        const gap = sample.time - quietSince;
        if (gap >= 2) interruptions.push(gap);
        quietSince = null;
      }
    }
    if (quietSince !== null && speech.length && speech[speech.length - 1].time - quietSince >= 2) {
      interruptions.push(speech[speech.length - 1].time - quietSince);
    }

    if (this.signalSamples.length < 100) this.testFeedback.push('The test recording was too short. Please repeat the full 30-second check.');
    if (ambientRms > 0.035) this.testFeedback.push('Steady background sound is high. Move to a quieter room and repeat the test.');
    if (speechRms < 0.012) this.testFeedback.push('Your voice is very quiet or was not detected. Move closer to the microphone and speak during the test.');
    if (clipping > 0.03) this.testFeedback.push('The sound is distorting. Move the microphone farther away or lower its input level.');
    if (interruptions.length) this.testFeedback.push('Long silence gaps were detected while speaking. Check the microphone connection and repeat the test if needed.');
    if (!this.testFeedback.length) this.testFeedback.push('The microphone signal looks clear and steady. Listen to the sample, then continue when ready.');
    this.state = 'testReview';
  }

  stopTest() {
    if (this.state !== 'testing') return;
    clearInterval(this.clock);
    this.clock = undefined;
    this.testElapsed = Math.min(30, Math.floor((Date.now() - this.startedAt) / 1000));
    if (this.recorder?.state === 'recording') this.recorder.stop();
    this.changeDetector.detectChanges();
  }

  retryTest() {
    this.state = 'testReady';
    this.testElapsed = 0;
    this.testFeedback = [];
    this.notice = '';
  }

  async retryRecording() {
    try { await this.capture.deleteDraft(); }
    catch {
      this.notice = 'The saved recording could not be removed, so it has been kept. Try again after checking browser storage.';
      return;
    }
    this.revokeAudioUrls();
    this.audioUrl = '';
    this.audioBlob = null;
    this.elapsed = 0;
    this.elapsedMs = 0;
    this.notice = '';
    this.state = 'idle';
    this.recoveredDraft = false;
  }

  async continueInterruptedRecording() {
    this.notice = '';
    if (!this.hasSpeakerConsent()) { this.notice = 'Current consent from each selected speaker is required before recording.'; return; }
    const availability = this.microphoneAvailabilityError();
    if (availability) { this.notice = availability; return; }
    try {
      await this.capture.beginAudioSegment();
      this.recoveredDraft = false;
      this.state = 'idle';
      await this.startRecording();
    } catch {
      this.notice = 'The saved recording could not be prepared for continuation. Your saved audio is still available.';
    }
  }

  download(url: string, mimeType: string, name: string) {
    const extension = mimeType.includes('ogg') ? 'ogg' : mimeType.includes('mp4') ? 'm4a' : mimeType.includes('wav') ? 'wav' : 'webm';
    const link = document.createElement('a');
    link.href = url;
    link.download = `${name}.${extension}`;
    link.click();
  }

  continueToRecording() {
    this.state = 'idle';
    this.notice = '';
    this.changeDetector.detectChanges();
  }

  async startRecording() {
    this.notice = '';
    if (!this.hasSpeakerConsent()) { this.notice = 'Current consent from each selected speaker is required before recording.'; return; }
    const availability = this.microphoneAvailabilityError();
    if (availability) { this.notice = availability; return; }
    try {
      if (!this.capture.audio) {
        this.elapsed = 0;
        this.elapsedMs = 0;
      }
      await this.beginCapture(false);
    } catch (e: any) {
      this.cleanupMedia();
      this.state = 'idle';
      this.notice = this.microphoneError(e);
      this.changeDetector.detectChanges();
    }
  }

  pause() {
    if (this.state !== 'recording' || this.recorder?.state !== 'recording') return;
    this.elapsedMs += Date.now() - this.startedAt;
    this.elapsed = Math.floor(this.elapsedMs / 1000);
    this.recorder.pause();
    this.state = 'paused';
    clearInterval(this.clock);
    this.clock = undefined;
  }

  resume() {
    if (this.state !== 'paused' || this.recorder?.state !== 'paused') return;
    this.recorder.resume();
    this.startedAt = Date.now();
    this.state = 'recording';
    this.clock = setInterval(() => {
      this.elapsed = Math.floor((this.elapsedMs + Date.now() - this.startedAt) / 1000);
      this.updateMicrophoneLevel();
      if (this.elapsed >= 3600) this.stop();
      this.changeDetector.detectChanges();
    }, 200);
  }

  stop() {
    if (!['recording', 'paused'].includes(this.state)) return;
    if (this.state === 'recording') {
      this.elapsedMs += Date.now() - this.startedAt;
      this.elapsed = Math.floor(this.elapsedMs / 1000);
    }
    clearInterval(this.clock);
    this.clock = undefined;
    if (this.recorder && this.recorder.state !== 'inactive') this.recorder.stop();
  }

  async onFile(event: Event) {
    const selection = ++this.fileSelection;
    if (!this.hasSpeakerConsent()) {
      this.notice = 'Current consent from each selected speaker is required before recording.';
      (event.target as HTMLInputElement).value = '';
      return;
    }
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    this.notice = '';
    if (!this.isSupportedAudio(file)) {
      (event.target as HTMLInputElement).value = '';
      this.notice = 'Choose a WAV, MP3, M4A, OGG, or WebM audio file.';
      return;
    }
    const candidateUrl = URL.createObjectURL(file);
    let duration = 0;
    try {
      duration = await this.readDuration(candidateUrl);
      if (selection !== this.fileSelection || this.destroyed) { URL.revokeObjectURL(candidateUrl); return; }
      if (!Number.isFinite(duration) || duration <= 0) throw new Error('Audio duration is invalid.');
    } catch {
      if (selection !== this.fileSelection || this.destroyed) { URL.revokeObjectURL(candidateUrl); return; }
      URL.revokeObjectURL(candidateUrl);
      (event.target as HTMLInputElement).value = '';
      this.notice = 'The selected audio duration could not be read.';
      return;
    }
    try { await this.capture.deleteDraft(); }
    catch {
      URL.revokeObjectURL(candidateUrl);
      this.capture.storageWarning.set('Browser storage could not safely replace the previous draft. Your saved work was kept.');
      return;
    }
    this.revokeAudioUrls();
    this.audioBlob = file;
    this.capture.audioName = file.name || this.fileNameFor(file.type);
    this.audioUrl = candidateUrl;
    this.elapsed = duration;
    this.elapsedMs = 0;
    this.capture.audio = null;
    this.state = 'done';
    const user = this.auth.user();
    if (user) {
      try {
        await this.capture.beginLocalDraft(user.id, 'recording');
        await this.capture.appendRecordingChunk(file, duration);
        await this.capture.updateDraft('recording', { elapsedSeconds: duration });
        await this.capture.restoreDraft(user.id);
        this.audioBlob = this.capture.audio;
        this.refreshAudioUrls();
        await this.capture.syncServerDraft(true);
      } catch {
        this.capture.storageWarning.set('The selected audio could not be saved to browser storage. Keep this page open and download a copy before leaving.');
      }
    }
  }

  private isSupportedAudio(file: File) {
    const supportedExtensions = /\.(wav|mp3|m4a|ogg|webm)$/i;
    const supportedMimeTypes = new Set(['audio/wav', 'audio/x-wav', 'audio/mpeg', 'audio/mp3', 'audio/mp4', 'audio/x-m4a', 'audio/ogg', 'audio/webm']);
    return supportedExtensions.test(file.name) || supportedMimeTypes.has(file.type.toLowerCase());
  }

  private readDuration(url: string): Promise<number> {
    return new Promise((resolve, reject) => {
      const audio = new Audio();
      audio.onloadedmetadata = () => resolve(Math.round(audio.duration));
      audio.onerror = () => reject(new Error('Audio duration is unavailable.'));
      audio.src = url;
    });
  }

  async proceed() {
    if (!this.hasSpeakerConsent()) { this.notice = 'Current consent from each selected speaker is required before recording.'; return; }
    if (!this.capture.language()) { this.notice = 'Choose a language project before recording.'; return; }
    if (!this.audioBlob) { this.notice = 'Record or select an audio file first.'; return; }
    if (this.elapsed < 1800 || this.elapsed > 3600) {
      this.notice = 'A submission must be between 30 and 60 minutes long.';
      return;
    }
    this.capture.audio = this.audioBlob;
    this.capture.transcript = '';
    try { await this.capture.updateDraft('transcription', { elapsedSeconds: this.elapsed }); }
    catch { this.capture.storageWarning.set('Your work is available now, but its saved copy could not be updated. Keep this browser tab open.'); }
    try { await this.capture.syncServerDraft(true); }
    catch { this.capture.storageWarning.set('Your recording is saved in this browser, but the server copy could not be updated yet.'); }
    this.router.navigate(['/transcription'], { queryParams: this.capture.activeDraft?.serverDraftId ? { draftId: this.capture.activeDraft.serverDraftId } : {} });
  }

  private formatTime(seconds: number) {
    return `${String(Math.floor(seconds / 3600)).padStart(2, '0')}:${String(Math.floor(seconds / 60) % 60).padStart(2, '0')}:${String(seconds % 60).padStart(2, '0')}`;
  }

  private hasSpeakerConsent() {
    return this.capture.speakers.length > 0 && this.capture.speakers.every(speaker => speaker.coreConsent === true);
  }

  private fileNameFor(mimeType: string) {
    return mimeType.includes('ogg') ? 'recording.ogg' : mimeType.includes('mp4') ? 'recording.m4a' : mimeType.includes('wav') ? 'recording.wav' : 'recording.webm';
  }

  private refreshAudioUrls() {
    this.revokeAudioUrls();
    this.audioSegmentUrls = this.capture.audioSegments.map(segment => URL.createObjectURL(segment));
    this.audioBlob = this.capture.audio;
    this.audioUrl = this.audioSegmentUrls[0] ?? (this.audioBlob ? URL.createObjectURL(this.audioBlob) : '');
  }

  private revokeAudioUrls() {
    for (const url of this.audioSegmentUrls) URL.revokeObjectURL(url);
    this.audioSegmentUrls = [];
    if (this.audioUrl) URL.revokeObjectURL(this.audioUrl);
    this.audioUrl = '';
  }

  private cleanupMedia() {
    this.activeStream?.getTracks().forEach(track => track.stop());
    this.activeStream = null;
    this.microphoneLevel = 0;
    this.analyser = null;
    if (this.audioContext && this.audioContext.state !== 'closed') void this.audioContext.close();
    this.audioContext = null;
  }

  ngOnDestroy() {
    this.destroyed = true;
    this.fileSelection++;
    clearInterval(this.clock);
    if (this.recorder && this.recorder.state !== 'inactive') this.recorder.stop();
    this.cleanupMedia();
    this.revokeAudioUrls();
    if (this.testAudioUrl) URL.revokeObjectURL(this.testAudioUrl);
  }
}
