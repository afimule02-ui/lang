import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../services/auth.service';
import { ApiService } from '../../services/api.service';
import { TranslateService } from '../../services/translate.service';
import { WorkspaceService } from '../../services/workspace.service';

@Component({
  selector: 'app-support',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './support.html'
})
export class SupportComponent implements OnInit {
  subject = '';
  firstMessage = '';
  replies: Record<number, string> = {};
  statusChoices: Record<number, string | undefined> = {};
  agentChoices: Record<number, number | null> = {};
  busy = '';
  error = '';
  notice = '';

  constructor(
    public auth: AuthService,
    public api: ApiService,
    public tr: TranslateService,
    public workspace: WorkspaceService
  ) {}

  async ngOnInit() {
    await this.auth.ready;
    if (this.auth.user()) await this.workspace.load().catch(() => {});
  }

  get role() { return this.auth.user()?.role; }
  get tickets() { return this.workspace.data()?.supportTickets ?? []; }
  get agents() { return (this.workspace.data()?.users ?? []).filter(user => user.role === 'agent' && user.active); }

  async createTicket() {
    this.error = '';
    this.notice = '';
    this.busy = 'create';
    try {
      await this.api.createSupportTicket(this.subject, this.firstMessage);
      this.subject = '';
      this.firstMessage = '';
      await this.workspace.load();
      this.notice = this.tr.t('Support request created.');
    } catch (error: any) {
      this.error = error?.error?.detail ?? this.tr.t('Could not create the support request.');
    } finally {
      this.busy = '';
    }
  }

  async sendReply(id: number) {
    const message = (this.replies[id] ?? '').trim();
    if (!message) return;
    this.error = '';
    this.notice = '';
    this.busy = `reply-${id}`;
    try {
      await this.api.sendSupportMessage(id, message);
      delete this.replies[id];
      await this.workspace.load();
    } catch (error: any) {
      this.error = error?.error?.detail ?? this.tr.t('Could not send the reply.');
    } finally {
      this.busy = '';
    }
  }

  async saveAdminUpdate(id: number, currentStatus: string, currentAgent: number | null) {
    this.error = '';
    this.notice = '';
    this.busy = `update-${id}`;
    const status = this.statusChoices[id] ?? currentStatus;
    const selectedAgent = this.agentChoices[id];
    const update: { status: string; assignedAgentId?: number } = { status };
    if (selectedAgent != null) update.assignedAgentId = selectedAgent;
    else if (currentAgent != null) update.assignedAgentId = currentAgent;
    try {
      await this.api.updateSupportTicket(id, update);
      await this.workspace.load();
      this.notice = this.tr.t('Support case updated.');
    } catch (error: any) {
      this.error = error?.error?.detail ?? this.tr.t('Could not update the support case.');
    } finally {
      this.busy = '';
    }
  }

  async escalate(id: number) {
    this.error = '';
    this.notice = '';
    this.busy = `update-${id}`;
    try {
      await this.api.escalateSupportTicket(id);
      await this.workspace.load();
      this.notice = this.tr.t('Support case escalated to the administrator.');
    } catch (error: any) {
      this.error = error?.error?.detail ?? this.tr.t('Could not escalate the support case.');
    } finally {
      this.busy = '';
    }
  }
}
