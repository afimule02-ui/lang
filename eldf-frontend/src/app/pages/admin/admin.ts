import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslateService } from '../../services/translate.service';
import { AuthService } from '../../services/auth.service';

@Component({ selector: 'app-admin', standalone: true, imports: [RouterLink], templateUrl: './admin.html' })
export class AdminComponent {
  tab: 'overview'|'users'|'finance'|'events' = 'overview';
  constructor(public tr: TranslateService, public auth: AuthService) {}
  get user() { return this.auth.user(); }
}
