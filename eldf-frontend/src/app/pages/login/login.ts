import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../services/auth.service';
import { TranslateService } from '../../services/translate.service';
import { AFRICAN_PHONE_COUNTRIES, internationalPhoneNumber, isValidPhoneNumber } from '../../services/africa-phone';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './login.html'
})
export class LoginComponent implements OnInit {
  role: 'contributor' | 'admin' | 'agent' = 'contributor';
  registered = false;
  phone = '';
  dialCode = '251';
  phoneCountries = AFRICAN_PHONE_COUNTRIES;
  password = '';
  showPassword = false;
  error = '';
  phoneFormatInvalid = false;
  loading = false;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    public auth: AuthService,
    public tr: TranslateService
  ) {}

  ngOnInit() {
    this.route.paramMap.subscribe(params => {
      const r = params.get('role');
      this.role = (r === 'admin' || r === 'agent') ? r : 'contributor';
    });
    this.route.queryParamMap.subscribe(params => this.registered = params.get('registered') === '1');
  }

  async submit() {
    this.error = '';
    this.phoneFormatInvalid = false;
    if (!isValidPhoneNumber(this.phone, this.dialCode)) {
      this.phoneFormatInvalid = true;
      this.error = this.tr.t('Enter a valid phone number with its country code.');
      return;
    }

    this.loading = true;
    try {
      const form = new FormData();
      form.append('phone', internationalPhoneNumber(this.phone, this.dialCode));
      form.append('password', this.password);
      const user = await this.auth.login(form);
      this.router.navigate([user.role === 'admin' ? '/admin' : user.role === 'agent' ? '/agent' : '/project']);
    } catch (e: any) {
      this.error = e?.error?.detail ?? e?.message ?? 'Login failed.';
    } finally {
      this.loading = false;
    }
  }

  onPhoneInputChanged() {
    this.phoneFormatInvalid = false;
    this.error = '';
  }
}
