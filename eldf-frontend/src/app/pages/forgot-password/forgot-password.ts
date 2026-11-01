import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslateService } from '../../services/translate.service';
import { ApiService } from '../../services/api.service';
import { AFRICAN_PHONE_COUNTRIES, internationalPhoneNumber } from '../../services/africa-phone';

@Component({ selector: 'app-forgot-password', standalone: true, imports: [FormsModule, RouterLink], templateUrl: './forgot-password.html' })
export class ForgotPasswordComponent {
  phone = '';
  dialCode = '251';
  phoneCountries = AFRICAN_PHONE_COUNTRIES;
  loading = false;
  error = '';
  sent = false;

  constructor(public tr: TranslateService, private api: ApiService) {}

  async submit() {
    this.loading = true;
    this.error = '';
    try {
      const phone = internationalPhoneNumber(this.phone, this.dialCode);
      await this.api.requestPasswordReset(phone);
      this.sent = true;
    } catch (error: any) {
      this.error = error?.error?.detail ?? 'Could not submit the password reset request. Try again later.';
    } finally {
      this.loading = false;
    }
  }
}
