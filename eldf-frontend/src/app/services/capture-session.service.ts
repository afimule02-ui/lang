import { Injectable, signal } from '@angular/core';
import { ApiService } from './api.service';

export type CaptureStage = 'recording' | 'transcription' | 'translation';

export interface LocalCaptureDraft {
  ownerId: number;
  language: string;
  speakers: any[];
  topics: string[];
  topicTitle?: string;
  recordingType: string;
  transcript: string;
  translation: string;
  stage: CaptureStage;
  elapsedSeconds: number;
  chunkCount: number;
  segmentCount: number;
  serverDraftId?: number;
  updatedAt: number;
}

interface StoredAudioChunk {
  key: string;
  ownerId: number;
  sequence: number;
  segment: number;
  blob: Blob;
}

@Injectable({ providedIn: 'root' })
export class CaptureSessionService {
  private readonly languageKey = 'eldf_project_language';
  private readonly countryKey = 'eldf_project_country';
  private readonly databaseName = 'afrilisan-capture-drafts';
  private database?: Promise<IDBDatabase>;

  language = signal(sessionStorage.getItem(this.languageKey) ?? '');
  country = signal(sessionStorage.getItem(this.countryKey) ?? '');
  savedDrafts = signal<LocalCaptureDraft[]>([]);
  storageWarning = signal('');
  activeOwnerId: number | null = null;
  activeDraft: LocalCaptureDraft | null = null;
  audio: Blob | null = null;
  audioSegments: Blob[] = [];
  audioName = 'recording.webm';
  transcript = '';
  translation = '';
  speakers: any[] = [];
  recordingType = 'Conversation';
  topics: string[] = [];
  topicTitle = '';

  constructor(private api: ApiService) {}

  setLanguage(language: string) {
    this.language.set(language);
    sessionStorage.setItem(this.languageKey, language);
  }

  setCountry(country: string) {
    this.country.set(country);
    sessionStorage.setItem(this.countryKey, country);
  }

  setSpeakers(speakers: any[]) { this.speakers = speakers.map(speaker => ({ ...speaker })); }
  setTopics(topics: string[]) { this.topics = [...topics]; }

  async loadDrafts(ownerId: number): Promise<LocalCaptureDraft[]> {
    if (this.activeOwnerId !== null && this.activeOwnerId !== ownerId) this.clearInMemory();
    this.activeOwnerId = ownerId;
    const db = await this.openDatabase();
    const tx = db.transaction('drafts', 'readonly');
    const draft = await this.request<LocalCaptureDraft | undefined>(tx.objectStore('drafts').get(ownerId));
    await this.transactionDone(tx);
    const drafts = draft ? [draft] : [];
    this.savedDrafts.set(drafts);
    return drafts;
  }

  async restoreDraft(ownerId: number): Promise<LocalCaptureDraft | null> {
    if (this.activeOwnerId !== null && this.activeOwnerId !== ownerId) this.clearInMemory();
    const db = await this.openDatabase();
    const read = db.transaction(['drafts', 'audioChunks'], 'readonly');
    const draft = await this.request<LocalCaptureDraft | undefined>(read.objectStore('drafts').get(ownerId));
    const chunks = await this.request<StoredAudioChunk[]>(read.objectStore('audioChunks').index('ownerId').getAll(ownerId));
    await this.transactionDone(read);
    if (!draft) {
      // A fresh speaker/topic setup is held in memory until the first recording
      // chunk creates a local draft. Do not erase that setup just because this
      // is the first visit to the recording page.
      this.activeDraft = null;
      this.audio = null;
      this.audioSegments = [];
      this.audioName = 'recording.webm';
      this.transcript = '';
      this.translation = '';
      this.activeOwnerId = ownerId;
      this.savedDrafts.set([]);
      return null;
    }
    chunks.sort((a, b) => a.sequence - b.sequence);
    this.activeOwnerId = ownerId;
    this.activeDraft = draft;
    this.language.set(draft.language);
    sessionStorage.setItem(this.languageKey, draft.language);
    this.speakers = draft.speakers.map(speaker => ({ ...speaker }));
    this.topics = [...draft.topics];
    this.topicTitle = draft.topicTitle ?? '';
    this.recordingType = draft.recordingType;
    this.transcript = draft.transcript;
    this.translation = draft.translation;
    const segments = new Map<number, StoredAudioChunk[]>();
    for (const chunk of chunks) segments.set(chunk.segment, [...(segments.get(chunk.segment) ?? []), chunk]);
    this.audioSegments = [...segments.entries()].sort(([a], [b]) => a - b).map(([, items]) =>
      new Blob(items.map(item => item.blob), { type: items[0].blob.type || 'audio/webm' }));
    this.audio = this.audioSegments.length ? new Blob(this.audioSegments, { type: this.audioSegments[0].type || 'audio/webm' }) : null;
    this.savedDrafts.set([draft]);
    return draft;
  }

  async restoreServerDraft(ownerId: number, serverDraft: any, audio: Blob | null): Promise<LocalCaptureDraft> {
    const existing = await this.restoreDraft(ownerId);
    if (existing && existing.serverDraftId !== serverDraft.id) {
      throw new Error('Another local draft is already saved for this account.');
    }
    if (existing && existing.serverDraftId === serverDraft.id) return existing;

    const speakers = (serverDraft.speakers ?? []).map((speaker: any) =>
      typeof speaker === 'string' ? { speakerId: speaker, coreConsent: false } : { ...speaker });
    const stage: CaptureStage = serverDraft.translation ? 'translation' : serverDraft.transcript ? 'transcription' : 'recording';
    const draft: LocalCaptureDraft = {
      ownerId, serverDraftId: serverDraft.id, language: serverDraft.language ?? '', speakers,
      topics: [], topicTitle: serverDraft.topicTitle ?? '', recordingType: serverDraft.recordingType ?? 'Conversation',
      transcript: serverDraft.transcript ?? '', translation: serverDraft.translation ?? '', stage,
      elapsedSeconds: serverDraft.durationSeconds ?? 0, chunkCount: audio ? 1 : 0,
      segmentCount: 1, updatedAt: Date.now()
    };
    const db = await this.openDatabase();
    const tx = db.transaction(['drafts', 'audioChunks'], 'readwrite');
    tx.objectStore('drafts').put(draft);
    if (audio?.size) tx.objectStore('audioChunks').put({
      key: `${ownerId}:0`, ownerId, sequence: 0, segment: 0, blob: audio
    } satisfies StoredAudioChunk);
    await this.transactionDone(tx);
    this.activeOwnerId = ownerId;
    this.activeDraft = draft;
    this.language.set(draft.language);
    sessionStorage.setItem(this.languageKey, draft.language);
    this.speakers = speakers.map((speaker: any) => ({ ...speaker }));
    this.topics = [];
    this.topicTitle = draft.topicTitle ?? '';
    this.recordingType = draft.recordingType;
    this.transcript = draft.transcript;
    this.translation = draft.translation;
    this.audioSegments = audio?.size ? [audio] : [];
    this.audio = audio?.size ? audio : null;
    this.audioName = audio ? this.fileNameFor(audio.type) : 'recording.webm';
    this.savedDrafts.set([draft]);
    return draft;
  }

  async beginLocalDraft(ownerId: number, stage: CaptureStage = 'recording') {
    this.activeOwnerId = ownerId;
    this.activeDraft = {
      ownerId,
      language: this.language(),
      speakers: this.speakers.map(speaker => ({ ...speaker })),
      topics: [...this.topics],
      topicTitle: this.topicTitle,
      recordingType: this.recordingType,
      transcript: this.transcript,
      translation: this.translation,
      stage,
      elapsedSeconds: 0,
      chunkCount: 0,
      segmentCount: 1,
      updatedAt: Date.now()
    };
    this.storageWarning.set('');
    await this.saveDraftMetadata();
  }

  async beginAudioSegment() {
    if (!this.activeDraft) return;
    this.activeDraft = { ...this.activeDraft, segmentCount: this.activeDraft.segmentCount + 1, updatedAt: Date.now() };
    await this.saveDraftMetadata();
  }

  async appendRecordingChunk(blob: Blob, elapsedSeconds: number) {
    if (!blob.size || !this.activeDraft) return;
    const db = await this.openDatabase();
    const tx = db.transaction(['drafts', 'audioChunks'], 'readwrite');
    const drafts = tx.objectStore('drafts');
    const chunks = tx.objectStore('audioChunks');
    const draft = await this.request<LocalCaptureDraft | undefined>(drafts.get(this.activeDraft.ownerId));
    if (!draft) { tx.abort(); throw new Error('Recording draft was not initialized.'); }
    const sequence = draft.chunkCount;
    const segment = Math.max(0, draft.segmentCount - 1);
    const chunk: StoredAudioChunk = { key: `${draft.ownerId}:${sequence}`, ownerId: draft.ownerId, sequence, segment, blob };
    chunks.put(chunk);
    draft.chunkCount++;
    draft.elapsedSeconds = Math.max(draft.elapsedSeconds, elapsedSeconds);
    draft.updatedAt = Date.now();
    draft.language = this.language();
    draft.speakers = this.speakers.map(speaker => ({ ...speaker }));
    draft.topics = [...this.topics];
    draft.topicTitle = this.topicTitle;
    draft.recordingType = this.recordingType;
    drafts.put(draft);
    await this.transactionDone(tx);
    const existingSegment = this.audioSegments[segment];
    this.audioSegments[segment] = existingSegment
      ? new Blob([existingSegment, blob], { type: existingSegment.type || blob.type || 'audio/webm' })
      : blob;
    this.audio = new Blob(this.audioSegments, { type: this.audioSegments[0]?.type || blob.type || 'audio/webm' });
    this.activeDraft = draft;
    this.savedDrafts.set([draft]);
  }

  async updateDraft(stage: CaptureStage, values: Partial<Pick<LocalCaptureDraft, 'elapsedSeconds' | 'transcript' | 'translation'>>) {
    if (!this.activeDraft) return;
    this.activeDraft = {
      ...this.activeDraft,
      ...values,
      language: this.language(),
      speakers: this.speakers.map(speaker => ({ ...speaker })),
      topics: [...this.topics],
      topicTitle: this.topicTitle,
      recordingType: this.recordingType,
      stage,
      updatedAt: Date.now()
    };
    await this.saveDraftMetadata();
  }

  async syncServerDraft(includeAudio = false) {
    if (!this.activeDraft) return null;
    const form = new FormData();
    if (this.activeDraft.serverDraftId) form.append('draft_id', String(this.activeDraft.serverDraftId));
    form.append('language', this.language());
    form.append('translation_language', '');
    form.append('transcript', this.transcript);
    form.append('translation', this.translation);
    form.append('recording_type', this.recordingType);
    form.append('topic_title', this.topicTitle);
    form.append('consent_reference', 'speaker-consent-v1');
    form.append('duration_seconds', String(this.activeDraft.elapsedSeconds));
    form.append('speakers_json', JSON.stringify(this.speakers.filter(speaker => speaker.speakerId).map(speaker => speaker.speakerId)));
    if (includeAudio && this.audioSegments.length) {
      if (this.audioSegments.length > 1) {
        this.audioSegments.forEach((segment, index) => form.append('audio_segments', segment, `recording-part-${index + 1}.${this.fileExtension(segment.type)}`));
      } else {
        form.append('file', this.audioSegments[0], `recording.${this.fileExtension(this.audioSegments[0].type)}`);
      }
    }
    const result = await this.api.saveDraft(form);
    this.activeDraft = { ...this.activeDraft, serverDraftId: result.id };
    await this.saveDraftMetadata();
    return result;
  }

  /** Save the current unfinished work on the server before starting a separate local session. */
  async parkCurrentDraft() {
    if (!this.activeDraft) return;
    await this.syncServerDraft(!!this.audioSegments.length);
    // Keep the server draft available from the contributor's Started work list, but release
    // this browser's single active-draft slot so a new recording cannot resume it by accident.
    await this.clear(false);
  }

  async deleteDraft(ownerId = this.activeOwnerId) {
    if (ownerId === null) return;
    const serverDraftId = this.activeOwnerId === ownerId ? this.activeDraft?.serverDraftId : undefined;
    const db = await this.openDatabase();
    const tx = db.transaction(['drafts', 'audioChunks'], 'readwrite');
    tx.objectStore('drafts').delete(ownerId);
    const index = tx.objectStore('audioChunks').index('ownerId');
    const cursorRequest = index.openKeyCursor(IDBKeyRange.only(ownerId));
    cursorRequest.onsuccess = () => {
      const cursor = cursorRequest.result;
      if (cursor) { tx.objectStore('audioChunks').delete(cursor.primaryKey); cursor.continue(); }
    };
    await this.transactionDone(tx);
    if (this.activeOwnerId === ownerId) {
      this.activeDraft = null;
      this.audio = null;
      this.audioSegments = [];
      this.transcript = '';
      this.translation = '';
      this.storageWarning.set('');
    }
    this.savedDrafts.set([]);
    if (serverDraftId) await this.api.deleteDraft(serverDraftId).catch(() => {});
  }

  async clear(deleteServer = true) {
    const ownerId = this.activeOwnerId;
    if (ownerId !== null) {
      if (deleteServer) await this.deleteDraft(ownerId);
      else {
        const db = await this.openDatabase();
        const tx = db.transaction(['drafts', 'audioChunks'], 'readwrite');
        tx.objectStore('drafts').delete(ownerId);
        const index = tx.objectStore('audioChunks').index('ownerId');
        const cursorRequest = index.openKeyCursor(IDBKeyRange.only(ownerId));
        cursorRequest.onsuccess = () => { const cursor = cursorRequest.result; if (cursor) { tx.objectStore('audioChunks').delete(cursor.primaryKey); cursor.continue(); } };
        await this.transactionDone(tx);
        this.savedDrafts.set([]);
      }
    }
    this.clearInMemory();
  }

  private fileExtension(mimeType: string) {
    return mimeType.includes('ogg') ? 'ogg' : mimeType.includes('mp4') ? 'm4a' : mimeType.includes('wav') ? 'wav' : 'webm';
  }

  private fileNameFor(mimeType: string) { return `recording.${this.fileExtension(mimeType)}`; }

  private async saveDraftMetadata() {
    if (!this.activeDraft) return;
    const db = await this.openDatabase();
    const tx = db.transaction('drafts', 'readwrite');
    tx.objectStore('drafts').put(this.activeDraft);
    await this.transactionDone(tx);
    this.savedDrafts.set([this.activeDraft]);
  }

  private openDatabase(): Promise<IDBDatabase> {
    this.database ??= new Promise((resolve, reject) => {
      const request = indexedDB.open(this.databaseName, 1);
      request.onupgradeneeded = () => {
        const db = request.result;
        db.createObjectStore('drafts', { keyPath: 'ownerId' });
        const chunks = db.createObjectStore('audioChunks', { keyPath: 'key' });
        chunks.createIndex('ownerId', 'ownerId', { unique: false });
      };
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error ?? new Error('Browser storage could not be opened.'));
    });
    return this.database;
  }

  private request<T>(request: IDBRequest<T>): Promise<T> {
    return new Promise((resolve, reject) => {
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error ?? new Error('Browser storage request failed.'));
    });
  }

  private transactionDone(tx: IDBTransaction): Promise<void> {
    return new Promise((resolve, reject) => {
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error ?? new Error('Browser storage transaction failed.'));
      tx.onabort = () => reject(tx.error ?? new Error('Browser storage transaction was aborted.'));
    });
  }

  private clearInMemory() {
    this.audio = null;
    this.audioSegments = [];
    this.audioName = 'recording.webm';
    this.transcript = '';
    this.translation = '';
    this.speakers = [];
    this.recordingType = 'Conversation';
    this.topics = [];
    this.topicTitle = '';
    this.activeDraft = null;
    this.activeOwnerId = null;
    this.storageWarning.set('');
  }
}
