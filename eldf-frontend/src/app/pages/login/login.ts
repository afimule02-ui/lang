import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../services/auth.service';
import { TranslateService } from '../../services/translate.service';
import { AFRICAN_PHONE_COUNTRIES, internationalPhoneNumber } from '../../services/africa-phone';

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
  error = '';
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
    this.loading = true;
    try {
      const form = new FormData();
      form.append('phone', internationalPhoneNumber(this.phone, this.dialCode));
      form.append('password', this.password);
      const user = await this.auth.login(form);
      if (user.role !== this.role) {
        await this.auth.logout(false);
        this.error = 'This account does not have access to this sign-in page.';
        return;
      }
      this.router.navigate([user.role === 'admin' ? '/admin' : user.role === 'agent' ? '/agent' : '/project']);
    } catch (e: any) {
      this.error = e?.error?.detail ?? e?.message ?? 'Login failed.';
    } finally {
      this.loading = false;
    }
  }
}
