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
  tab: 'overview'|'users'|'finance'|'events' = 'overview';
  savingUserId: number | null = null;
  actionError = '';
  financeNotes: Record<number, string> = {};
  proofReferences: Record<number, string> = {};
  paymentBusyId: number | null = null;
  resetBusyId: number | null = null;
  resetError = '';
  resetPasswords: Record<number, string> = {};
  constructor(public tr: TranslateService, public auth: AuthService, public workspace: WorkspaceService, private api: ApiService) {}
  get user() { return this.auth.user(); }
  get agents() { return (this.workspace.data()?.users ?? []).filter(account => account.role === 'agent' && account.active); }
  accountForReset(event: { entity: string; entityId: string }) {
    return event.entity === 'User' ? (this.workspace.data()?.users ?? []).find(account => account.id.toString() === event.entityId) : undefined;
  }
  async ngOnInit() {
    await this.auth.ready;
    if (this.auth.user()?.role === 'admin') await this.workspace.load().catch(() => {});
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
}
