import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../services/auth.service';
import { TranslateService } from '../../services/translate.service';
import { ApiService } from '../../services/api.service';
import { WorkspaceService, WorkspaceSubmission } from '../../services/workspace.service';
import { ActivatedRoute, Router } from '@angular/router';

@Component({ selector: 'app-review', standalone: true, imports: [CommonModule, FormsModule], templateUrl: './review.html' })
export class ReviewComponent implements OnInit {
  notes: Record<number, string> = {};
  error = '';
  message = '';
  reviewingId: number | null = null;
  assignmentBusyId: number | null = null;
  assignedAgents: Record<number, number | null> = {};
  private readonly initializedAssignments = new Set<number>();

  constructor(public tr: TranslateService, public auth: AuthService, public api: ApiService, public workspace: WorkspaceService,
    private route: ActivatedRoute, private router: Router) {}

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
    const items = (this.workspace.data()?.submissions ?? []).filter(item => allowed.includes(item.status));
    for (const item of items) {
      if (!this.initializedAssignments.has(item.id)) {
        this.assignedAgents[item.id] = item.assignedAgentId ?? null;
        this.initializedAssignments.add(item.id);
      }
    }
    return items;
  }

  get agents() { return (this.workspace.data()?.users ?? []).filter(user => user.role === 'agent' && user.active); }

  async assign(item: WorkspaceSubmission) {
    this.assignmentBusyId = item.id;
    this.error = '';
    this.message = '';
    try {
      await this.api.assignSubmissionReview(item.id, this.assignedAgents[item.id] ?? null);
      await this.workspace.load();
      this.message = this.tr.t('Review task assignment updated.');
    } catch (error: any) {
      this.error = error?.error?.detail ?? this.tr.t('Could not assign this review task.');
    } finally { this.assignmentBusyId = null; }
  }

  async decide(item: WorkspaceSubmission, decision: 'accept' | 'return' | 'approve' | 'reject') {
    this.error = '';
    this.message = '';
    this.reviewingId = item.id;
    try {
      const comment = this.notes[item.id] ?? '';
      let result: any;
      if (this.auth.user()?.role === 'agent') {
        result = await this.api.agentReview(item.id, decision as 'accept' | 'return', comment);
      } else {
        result = await this.api.adminReview(item.id, decision as 'approve' | 'return' | 'reject', comment, decision === 'reject' ? comment : '');
      }
      const acceptedStatus = this.auth.user()?.role === 'agent' ? 'Awaiting admin review' : 'Approved';
      if ((decision === 'accept' || decision === 'approve') && result?.status !== acceptedStatus) {
        throw new Error('The server did not confirm that the review was accepted.');
      }
      delete this.notes[item.id];
      if (decision === 'accept' || decision === 'approve') {
        const role = this.auth.user()?.role;
        const expectedReturn = role === 'agent' ? '/agent' : '/admin';
        const requestedReturn = this.route.snapshot.queryParamMap.get('returnTo');
        await this.router.navigateByUrl(requestedReturn === expectedReturn ? requestedReturn : expectedReturn);
        return;
      }
      try { await this.workspace.load(); }
      catch { this.message = this.tr.t('Review decision was saved, but the queue could not refresh. Reopen it to see the latest status.'); return; }
      this.message = this.tr.t('Review saved.');
    } catch (error: any) {
      this.error = error?.error?.detail ?? this.tr.t('Could not save the review.');
    } finally {
      this.reviewingId = null;
    }
  }
}
