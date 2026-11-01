import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslateService } from '../../services/translate.service';
import { ApiService } from '../../services/api.service';

@Component({ selector: 'app-change-password', standalone: true, imports: [FormsModule, RouterLink], templateUrl: './change-password.html' })
export class ChangePasswordComponent {
  currentPassword = '';
  newPassword = '';
  confirmPassword = '';
  error = '';
  message = '';
  loading = false;

  constructor(public tr: TranslateService, private api: ApiService) {}

  async submit() {
    this.error = '';
    this.message = '';
    if (this.newPassword.length < 10) { this.error = this.tr.t('Use a password with at least 10 characters.'); return; }
    if (this.newPassword !== this.confirmPassword) { this.error = this.tr.t('The passwords do not match.'); return; }
    this.loading = true;
    try {
      await this.api.changePassword(this.currentPassword, this.newPassword);
      this.currentPassword = this.newPassword = this.confirmPassword = '';
      this.message = this.tr.t('Password changed successfully.');
    } catch (error: any) {
      this.error = error?.error?.detail ?? 'Could not change the password.';
    } finally {
      this.loading = false;
    }
  }
}
