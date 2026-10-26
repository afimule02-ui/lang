import { Component } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../services/auth.service';
import { TranslateService } from '../../services/translate.service';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './register.html'
})
export class RegisterComponent {
  name = '';
  phone = '';
  password = '';
  language: 'am' | 'en' = 'am';
  referralCode = '';
  staffCode = '';
  terms = false;
  consent = false;
  error = '';
  loading = false;

  constructor(public tr: TranslateService, private auth: AuthService, private router: Router) {}

  async submit() {
    if (!this.terms || !this.consent) { this.error = this.tr.t('Please check both consent boxes to continue.'); return; }
    this.error = '';
    this.loading = true;
    try {
      const form = new FormData();
      form.append('name', this.name);
      form.append('phone', this.phone.startsWith('+') ? this.phone : '+251' + this.phone);
      form.append('password', this.password);
      form.append('language', this.language);
      form.append('referral_code', this.referralCode);
      form.append('staff_code', this.staffCode);
      form.append('terms', 'true');
      form.append('consent', 'true');
      const user = await this.auth.register(form);
      this.router.navigate([user.role === 'admin' ? '/admin' : user.role === 'agent' ? '/agent' : '/project']);
    } catch (e: any) {
      this.error = e?.error?.detail ?? e?.message ?? 'Registration failed.';
    } finally {
      this.loading = false;
    }
  }
}
