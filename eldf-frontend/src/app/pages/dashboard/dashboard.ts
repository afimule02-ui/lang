import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { TranslateService } from '../../services/translate.service';
import { WorkspaceService } from '../../services/workspace.service';
import { ApiService } from '../../services/api.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './dashboard.html'
})
export class DashboardComponent implements OnInit {
  payoutAmount = 1000;
  payoutError = '';
  payoutMessage = '';
  submittingPayout = false;
  constructor(public auth: AuthService, public tr: TranslateService, public workspace: WorkspaceService, private api: ApiService) {}
  get user() { return this.auth.user(); }

  async ngOnInit() {
    await this.auth.ready;
    if (this.auth.user()) await this.workspace.load().catch(() => {});
  }

  count(status: string) {
    return this.workspace.data()?.submissions.filter(item => item.status.toLowerCase().includes(status.toLowerCase())).length ?? 0;
  }

  async requestPayout() {
    this.payoutError = '';
    this.payoutMessage = '';
    this.submittingPayout = true;
    try {
      await this.api.requestPayment(Number(this.payoutAmount));
      await this.workspace.load();
      this.payoutMessage = 'Payout request sent for review.';
    } catch (error: any) {
      this.payoutError = error?.error?.detail ?? 'Could not request a payout.';
    } finally {
      this.submittingPayout = false;
    }
  }
}
