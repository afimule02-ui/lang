import { ChangeDetectorRef, Component, OnDestroy } from '@angular/core';
import { Router } from '@angular/router';
import { TranslateService } from '../../services/translate.service';
import { CaptureSessionService } from '../../services/capture-session.service';

type RecordState = 'testReady' | 'testing' | 'testReview' | 'idle' | 'recording' | 'paused' | 'done';
interface SignalSample { time: number; rms: number; peak: number; }

@Component({ selector: 'app-record', standalone: true, imports: [], templateUrl: './record.html' })
export class RecordComponent implements OnDestroy {
  state: RecordState = 'testReady';
  elapsed = 0;
  testElapsed = 0;
  notice = '';
  testFeedback: string[] = [];
  testAudioUrl = '';
  audioBlob: Blob | null = null;
  audioUrl = '';
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

  constructor(public tr: TranslateService, public capture: CaptureSessionService, private router: Router,
    private changeDetector: ChangeDetectorRef) {}

  get clock_display() { return this.formatTime(this.elapsed); }
  get test_clock_display() { return this.formatTime(Math.max(0, 30 - this.testElapsed)); }

  async startTest() {
    this.notice = '';
    this.testFeedback = [];
    this.testElapsed = 0;
    this.signalSamples = [];
    if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === 'undefined') {
      this.notice = 'Microphone testing is not supported by this browser.';
      return;
    }
    try {
      await this.beginCapture(true);
    } catch (e: any) {
      this.cleanupMedia();
      this.state = 'testReady';
      this.notice = e?.message ?? 'Microphone access denied.';
    }
  }

  private async beginCapture(isTest: boolean) {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: {
      echoCancellation: false, noiseSuppression: false, autoGainControl: false
    } });
    this.activeStream = stream;
    const AudioContextClass = window.AudioContext;
    if (AudioContextClass) {
      this.audioContext = new AudioContextClass();
      await this.audioContext.resume();
      this.analyser = this.audioContext.createAnalyser();
      this.analyser.fftSize = 1024;
      this.audioContext.createMediaStreamSource(stream).connect(this.analyser);
      this.waveform = new Uint8Array(this.analyser.fftSize);
    }
    this.chunks = [];
    const mimeType = ['audio/webm;codecs=opus', 'audio/webm', 'audio/ogg;codecs=opus']
      .find(type => MediaRecorder.isTypeSupported(type));
    this.recorder = new MediaRecorder(stream, mimeType ? { mimeType } : undefined);
    this.recorder.ondataavailable = event => { if (event.data.size) this.chunks.push(event.data); };
    this.recorder.onstop = () => {
      const blob = new Blob(this.chunks, { type: this.recorder?.mimeType || 'audio/webm' });
      this.cleanupMedia();
      if (isTest) {
        if (this.testAudioUrl) URL.revokeObjectURL(this.testAudioUrl);
        this.testAudioUrl = URL.createObjectURL(blob);
        this.reviewTest();
      } else {
        this.audioBlob = blob;
        if (this.audioUrl) URL.revokeObjectURL(this.audioUrl);
        this.audioUrl = URL.createObjectURL(blob);
        this.state = 'done';
      }
      this.changeDetector.detectChanges();
    };
    this.recorder.start(200);
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

  continueToRecording() {
    this.state = 'idle';
    this.notice = '';
    this.changeDetector.detectChanges();
  }

  async startRecording() {
    this.notice = '';
    try {
      this.elapsed = 0;
      this.elapsedMs = 0;
      await this.beginCapture(false);
    } catch (e: any) {
      this.cleanupMedia();
      this.state = 'idle';
      this.notice = e?.message ?? 'Microphone access denied.';
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
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    if (this.audioUrl) URL.revokeObjectURL(this.audioUrl);
    this.audioBlob = file;
    this.audioUrl = URL.createObjectURL(file);
    this.state = 'idle';
    this.notice = '';
    try { this.elapsed = await this.readDuration(this.audioUrl); }
    catch { this.notice = 'The selected audio duration could not be read.'; return; }
    this.state = 'done';
  }

  private readDuration(url: string): Promise<number> {
    return new Promise((resolve, reject) => {
      const audio = new Audio();
      audio.onloadedmetadata = () => resolve(Math.round(audio.duration));
      audio.onerror = () => reject(new Error('Audio duration is unavailable.'));
      audio.src = url;
    });
  }

  proceed() {
    if (!this.capture.language()) { this.notice = 'Choose a language project before recording.'; return; }
    if (!this.audioBlob) { this.notice = 'Record or select an audio file first.'; return; }
    if (this.elapsed < 1800 || this.elapsed > 3600) {
      this.notice = 'A submission must be between 30 and 60 minutes long.';
      return;
    }
    this.capture.audio = this.audioBlob;
    this.capture.transcript = '';
    this.router.navigate(['/transcription']);
  }

  private formatTime(seconds: number) {
    return `${String(Math.floor(seconds / 3600)).padStart(2, '0')}:${String(Math.floor(seconds / 60) % 60).padStart(2, '0')}:${String(seconds % 60).padStart(2, '0')}`;
  }

  private cleanupMedia() {
    this.activeStream?.getTracks().forEach(track => track.stop());
    this.activeStream = null;
    this.analyser = null;
    if (this.audioContext && this.audioContext.state !== 'closed') void this.audioContext.close();
    this.audioContext = null;
  }

  ngOnDestroy() {
    clearInterval(this.clock);
    if (this.recorder && this.recorder.state !== 'inactive') this.recorder.stop();
    this.cleanupMedia();
    if (this.audioUrl) URL.revokeObjectURL(this.audioUrl);
    if (this.testAudioUrl) URL.revokeObjectURL(this.testAudioUrl);
  }
}
