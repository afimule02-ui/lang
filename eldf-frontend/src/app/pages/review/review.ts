import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../services/auth.service';
import { TranslateService } from '../../services/translate.service';
import { ApiService } from '../../services/api.service';
import { WorkspaceService, WorkspaceSubmission } from '../../services/workspace.service';

@Component({ selector: 'app-review', standalone: true, imports: [CommonModule, FormsModule], templateUrl: './review.html' })
export class ReviewComponent implements OnInit {
  notes: Record<number, string> = {};
  error = '';
  message = '';
  reviewingId: number | null = null;

  constructor(public tr: TranslateService, public auth: AuthService, public api: ApiService, public workspace: WorkspaceService) {}

  async ngOnInit() {
    await this.auth.ready;
    if (this.auth.user()) await this.workspace.load().catch(() => {});
  }

  get pending(): WorkspaceSubmission[] {
    const role = this.auth.user()?.role;
    const allowed = role === 'agent'
      ? ['Awaiting agent review']
      : role === 'admin'
        ? ['Awaiting admin review', 'Awaiting review', 'Returned by admin']
        : [];
    return (this.workspace.data()?.submissions ?? []).filter(item => allowed.includes(item.status));
  }

  async decide(item: WorkspaceSubmission, decision: 'accept' | 'return' | 'approve' | 'reject') {
    this.error = '';
    this.message = '';
    this.reviewingId = item.id;
    try {
      const comment = this.notes[item.id] ?? '';
      if (this.auth.user()?.role === 'agent') {
        await this.api.agentReview(item.id, decision as 'accept' | 'return', comment);
      } else {
        await this.api.adminReview(item.id, decision as 'approve' | 'return' | 'reject', comment, decision === 'reject' ? comment : '');
      }
      delete this.notes[item.id];
      await this.workspace.load();
      this.message = this.tr.t('Review saved.');
    } catch (error: any) {
      this.error = error?.error?.detail ?? this.tr.t('Could not save the review.');
    } finally {
      this.reviewingId = null;
    }
  }
}
