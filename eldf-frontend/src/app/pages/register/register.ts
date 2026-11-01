import { Component } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../services/auth.service';
import { TranslateService } from '../../services/translate.service';
import { AFRICAN_PHONE_COUNTRIES, internationalPhoneNumber } from '../../services/africa-phone';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './register.html'
})
export class RegisterComponent {
  name = '';
  phone = '';
  dialCode = '251';
  phoneCountries = AFRICAN_PHONE_COUNTRIES;
  password = '';
  passwordConfirm = '';
  language: 'am' | 'en' = 'am';
  referralCode = '';
  staffCode = '';
  terms = false;
  consent = false;
  error = '';
  loading = false;

  constructor(public tr: TranslateService, private auth: AuthService, private router: Router) {}

  async submit() {
    if (this.password.length < 10) { this.error = this.tr.t('Use a password with at least 10 characters.'); return; }
    if (this.password !== this.passwordConfirm) { this.error = this.tr.t('The passwords do not match.'); return; }
    if (!this.terms || !this.consent) { this.error = this.tr.t('Please check both consent boxes to continue.'); return; }
    this.error = '';
    this.loading = true;
    try {
      const form = new FormData();
      form.append('name', this.name);
      form.append('phone', internationalPhoneNumber(this.phone, this.dialCode));
      form.append('password', this.password);
      form.append('language', this.language);
      form.append('referral_code', this.referralCode);
      form.append('staff_code', this.staffCode);
      form.append('terms', 'true');
      form.append('consent', 'true');
      await this.auth.register(form);
      this.router.navigate(['/login/contributor'], { queryParams: { registered: '1' } });
    } catch (e: any) {
      this.error = e?.error?.detail ?? e?.message ?? 'Registration failed.';
    } finally {
      this.loading = false;
    }
  }
}
