import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { TranslateService } from '../../services/translate.service';
import { WorkspaceService } from '../../services/workspace.service';
import { ApiService } from '../../services/api.service';
import { CaptureSessionService } from '../../services/capture-session.service';

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
  constructor(public auth: AuthService, public tr: TranslateService, public workspace: WorkspaceService, public capture: CaptureSessionService, private api: ApiService) {}
  get user() { return this.auth.user(); }
  get serverDrafts() {
    const localIds = new Set(this.capture.savedDrafts().map(draft => draft.serverDraftId).filter((id): id is number => !!id));
    return (this.workspace.data()?.drafts ?? []).filter(draft => !localIds.has(draft.id));
  }

  async ngOnInit() {
    await this.auth.ready;
    const user = this.auth.user();
    if (user) {
      await Promise.all([
        this.workspace.load().catch(() => {}),
        this.capture.loadDrafts(user.id).catch(() => {
          this.capture.storageWarning.set('Saved work in this browser could not be listed. Check browser storage permissions and do not clear site data.');
          return [];
        })
      ]);
    }
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
