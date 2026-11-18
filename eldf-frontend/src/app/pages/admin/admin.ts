import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslateService } from '../../services/translate.service';
import { AuthService } from '../../services/auth.service';
import { WorkspaceService } from '../../services/workspace.service';
import { ApiService } from '../../services/api.service';

@Component({ selector: 'app-admin', standalone: true, imports: [CommonModule, FormsModule, RouterLink], templateUrl: './admin.html' })
export class AdminComponent implements OnInit {
  tab: 'overview'|'users'|'finance'|'events'|'consents'|'completed'|'drafts'|'settings' = 'overview';
  savingUserId: number | null = null;
  actionError = '';
  financeNotes: Record<number, string> = {};
  proofReferences: Record<number, string> = {};
  paymentBusyId: number | null = null;
  resetBusyId: number | null = null;
  resetError = '';
  resetPasswords: Record<number, string> = {};
  consentRecords: any[] = [];
  consentError = '';
  draftAssignments: Record<number, number | null> = {};
  profileName = '';
  profilePhone = '';
  profileCurrentPassword = '';
  profileBusy = false;
  profileMessage = '';
  profileError = '';
  selectedDatasetItems: Record<number, boolean> = {};
  constructor(public tr: TranslateService, public auth: AuthService, public workspace: WorkspaceService, private api: ApiService) {}
  get user() { return this.auth.user(); }
  get agents() { return (this.workspace.data()?.users ?? []).filter(account => account.role === 'agent' && account.active); }
  get completedSubmissions() { return (this.workspace.data()?.submissions ?? []).filter(item => item.status === 'Approved'); }
  get selectedDatasetCount() { return Object.values(this.selectedDatasetItems).filter(Boolean).length; }
  accountForReset(event: { entity: string; entityId: string }) {
    return event.entity === 'User' ? (this.workspace.data()?.users ?? []).find(account => account.id.toString() === event.entityId) : undefined;
  }
  async ngOnInit() {
    await this.auth.ready;
    const user = this.auth.user();
    if (user?.role === 'admin') {
      this.profileName = user.name;
      this.profilePhone = user.phone;
      await this.workspace.load().catch(() => {});
      this.loadDraftAssignments();
    }
  }

  private loadDraftAssignments() {
    for (const draft of this.workspace.data()?.drafts ?? []) this.draftAssignments[draft.id] = draft.assignedUserId ?? draft.ownerUserId;
  }

  get assignableContributors() {
    return (this.workspace.data()?.users ?? []).filter(account => account.role === 'contributor' && account.active);
  }

  async reassignDraft(draft: { id: number }, assignedUserId: number | null) {
    this.actionError = '';
    try {
      await this.api.reassignDraft(draft.id, assignedUserId);
      await this.workspace.load();
      this.loadDraftAssignments();
      this.actionError = 'Interrupted work assignment updated.';
    } catch (error: any) {
      this.actionError = error?.error?.detail ?? 'Could not reassign this interrupted work.';
    }
  }

  async saveProfile() {
    this.profileError = '';
    this.profileMessage = '';
    if (this.profileName.trim().length < 2 || !this.profileCurrentPassword) {
      this.profileError = 'Enter your name and current password to update administrator sign-in details.';
      return;
    }
    this.profileBusy = true;
    try {
      const result = await this.api.updateAdminProfile({ name: this.profileName.trim(), phone: this.profilePhone, currentPassword: this.profileCurrentPassword });
      this.auth.updateCurrentUser(result.user);
      this.profileName = result.user.name;
      this.profilePhone = result.user.phone;
      this.profileCurrentPassword = '';
      this.profileMessage = 'Administrator name and sign-in phone updated.';
    } catch (error: any) {
      this.profileError = error?.error?.detail ?? 'Could not update administrator settings.';
    } finally {
      this.profileBusy = false;
    }
  }

  downloadDataset() {
    const ids = Object.entries(this.selectedDatasetItems).filter(([, selected]) => selected).map(([id]) => Number(id));
    if (!ids.length || ids.length > 100) {
      this.actionError = 'Select between 1 and 100 approved submissions for export.';
      return;
    }
    this.actionError = '';
    window.location.assign(`/api/admin/dataset-export?ids=${encodeURIComponent(ids.join(','))}`);
  }

  async saveUser(account: { id: number; name: string; role: string; active: boolean; agentId?: number | null }) {
    this.savingUserId = account.id;
    this.actionError = '';
    try {
      await this.api.updateAdminUser(account.id, account);
      await this.workspace.load();
    } catch (error: any) {
      this.actionError = error?.error?.detail ?? 'Could not update this account.';
      await this.workspace.load().catch(() => {});
    } finally {
      this.savingUserId = null;
    }
  }

  async decidePayment(payment: { id: number }, decision: 'approve' | 'reject' | 'paid') {
    this.actionError = '';
    this.paymentBusyId = payment.id;
    try {
      await this.api.reviewPaymentAsAdmin(payment.id, decision, this.financeNotes[payment.id] ?? '', this.proofReferences[payment.id] ?? '');
      await this.workspace.load();
      delete this.financeNotes[payment.id];
      if (decision === 'paid') delete this.proofReferences[payment.id];
    } catch (error: any) {
      this.actionError = error?.error?.detail ?? 'Could not update the payment request.';
    } finally {
      this.paymentBusyId = null;
    }
  }

  async issueResetPassword(accountId: number) {
    this.resetBusyId = accountId;
    this.resetError = '';
    try {
      const result = await this.api.resetUserPassword(accountId);
      this.resetPasswords[accountId] = result.temporaryPassword;
      await this.workspace.load();
    } catch (error: any) {
      this.resetError = error?.error?.detail ?? 'Could not issue a password reset.';
    } finally {
      this.resetBusyId = null;
    }
  }

  async showConsents() {
    this.tab = 'consents';
    this.consentError = '';
    try { this.consentRecords = await this.api.speakerConsents(); }
    catch (error: any) { this.consentError = error?.error?.detail ?? 'Could not load speaker consent records.'; }
  }

  async shareDeliveryLink(id: number) {
    this.actionError = '';
    try {
      const url = new URL(`/api/submissions/${id}/delivery`, window.location.origin).toString();
      await navigator.clipboard.writeText(url);
      this.actionError = 'Protected download link copied. Recipients still need an administrator sign-in.';
    } catch {
      this.actionError = 'Could not copy the protected link. Copy the download address from the link menu instead.';
    }
  }
}
