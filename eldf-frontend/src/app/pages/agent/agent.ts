import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslateService } from '../../services/translate.service';
import { AuthService } from '../../services/auth.service';
import { WorkspaceService } from '../../services/workspace.service';
import { ApiService } from '../../services/api.service';

@Component({ selector: 'app-agent', standalone: true, imports: [CommonModule, FormsModule, RouterLink], templateUrl: './agent.html' })
export class AgentComponent implements OnInit {
  paymentNotes: Record<number, string> = {};
  paymentError = '';
  paymentBusyId: number | null = null;
  constructor(public tr: TranslateService, public auth: AuthService, public workspace: WorkspaceService, private api: ApiService) {}
  get user() { return this.auth.user(); }
  async ngOnInit() {
    await this.auth.ready;
    if (this.auth.user()?.role === 'agent') await this.workspace.load().catch(() => {});
  }

  async reviewPayment(paymentId: number, decision: 'accept' | 'return') {
    this.paymentBusyId = paymentId;
    this.paymentError = '';
    try {
      await this.api.reviewPaymentAsAgent(paymentId, decision, this.paymentNotes[paymentId] ?? '');
      await this.workspace.load();
      delete this.paymentNotes[paymentId];
    } catch (error: any) {
      this.paymentError = error?.error?.detail ?? 'Could not review this payout request.';
    } finally {
      this.paymentBusyId = null;
    }
  }
}
