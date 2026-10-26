import { Component, OnDestroy } from '@angular/core';
import { Router } from '@angular/router';
import { TranslateService } from '../../services/translate.service';
import { ApiService } from '../../services/api.service';

@Component({ selector: 'app-record', standalone: true, imports: [], templateUrl: './record.html' })
export class RecordComponent implements OnDestroy {
  state: 'idle'|'recording'|'paused'|'done' = 'idle';
  elapsed = 0;
  notice = '';
  audioBlob: Blob | null = null;
  audioUrl = '';
  private recorder: MediaRecorder | null = null;
  private chunks: Blob[] = [];
  private clock: any = null;
  private started = 0;

  constructor(public tr: TranslateService, private api: ApiService, private router: Router) {}

  get clock_display() {
    const t = this.elapsed;
    return `${String(Math.floor(t/3600)).padStart(2,'0')}:${String(Math.floor(t/60)%60).padStart(2,'0')}:${String(t%60).padStart(2,'0')}`;
  }

  async startRecording() {
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      this.chunks = [];
      this.recorder = new MediaRecorder(stream);
      this.recorder.ondataavailable = e => { if (e.data.size) this.chunks.push(e.data); };
      this.recorder.onstop = () => {
        this.audioBlob = new Blob(this.chunks, { type: 'audio/webm' });
        this.audioUrl = URL.createObjectURL(this.audioBlob);
        this.state = 'done';
        stream.getTracks().forEach(t => t.stop());
      };
      this.recorder.start(250);
      this.started = Date.now();
      this.elapsed = 0;
      this.state = 'recording';
      this.clock = setInterval(() => this.elapsed = Math.floor((Date.now() - this.started) / 1000), 1000);
    } catch (e: any) {
      this.notice = e.message ?? 'Microphone access denied.';
    }
  }

  pause() { this.recorder?.pause(); this.state = 'paused'; clearInterval(this.clock); }
  resume() { this.recorder?.resume(); this.state = 'recording'; this.started = Date.now() - this.elapsed * 1000; this.clock = setInterval(() => this.elapsed = Math.floor((Date.now() - this.started) / 1000), 1000); }
  stop() { clearInterval(this.clock); this.recorder?.stop(); }

  onFile(e: Event) {
    const file = (e.target as HTMLInputElement).files?.[0];
    if (!file) return;
    if (this.audioUrl) URL.revokeObjectURL(this.audioUrl);
    this.audioBlob = file;
    this.audioUrl = URL.createObjectURL(file);
    this.state = 'done';
  }

  proceed() { this.router.navigate(['/transcription']); }

  ngOnDestroy() { clearInterval(this.clock); }
}
