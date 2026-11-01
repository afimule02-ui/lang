import { Injectable, signal } from '@angular/core';
import { ApiService } from './api.service';

export interface WorkspaceSubmission {
  id: number;
  contributorName: string;
  contributorPhone: string;
  language: string;
  durationSeconds: number;
  audioUrl: string;
  transcript: string;
  translation: string;
  status: string;
  agentComment: string;
  adminComment: string;
  rejectionReason: string;
  createdAt: string;
  reviewHistory: Array<{ id: number; role: string; decision: string; comment: string; createdAt: string }>;
}

export interface WorkspaceData {
  role: 'contributor' | 'agent' | 'admin';
  submissions: WorkspaceSubmission[];
  drafts?: Array<{ id: number; language: string; updatedAt: string }>;
  users?: Array<{ id: number; name: string; phone: string; role: string; active: boolean; agentId?: number | null; referralCode?: string; registeredAt: string }>;
  contributors?: Array<{ id: number; name: string; phone: string; registeredAt: string }>;
  paymentRequests?: Array<{ id: number; userId: number; userName: string; userPhone: string; amount: number; currency: string; status: string; reviewNote: string; proofReference: string; requestedAt: string }>;
  events?: Array<{ id: number; type: string; role: string; details: string; entity: string; entityId: string; createdAt: string }>;
  report?: { pendingReview: number; approved: number; returned: number; contributors: number; audioHours: number };
  balance?: number;
  referralCode?: string;
  teamDrafts?: Array<{ id: number; contributorId: number; contributorName: string; language: string; durationSeconds: number; hasAudio: boolean; hasTranscript: boolean; hasTranslation: boolean; updatedAt: string }>;
}

@Injectable({ providedIn: 'root' })
export class WorkspaceService {
  data = signal<WorkspaceData | null>(null);
  loading = signal(false);
  error = signal('');

  constructor(private api: ApiService) {}

  async load() {
    this.loading.set(true);
    this.error.set('');
    try {
      const data = await this.api.workspace<WorkspaceData>();
      this.data.set(data);
      return data;
    } catch (error: any) {
      this.error.set(error?.error?.detail ?? 'Could not load your workspace. Please sign in and try again.');
      throw error;
    } finally {
      this.loading.set(false);
    }
  }
}
