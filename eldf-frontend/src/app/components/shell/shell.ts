import { Component, computed } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive, Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { AuthService } from '../../services/auth.service';
import { TranslateService } from '../../services/translate.service';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, CommonModule],
  templateUrl: './shell.html',
  styleUrl: './shell.scss'
})
export class ShellComponent {
  sidebarOpen = false;

  constructor(public auth: AuthService, public tr: TranslateService, private router: Router) {}

  get user() { return this.auth.user(); }
  get lang() { return this.auth.lang(); }

  setLang(l: 'am' | 'en') {
    this.auth.setLang(l);
    this.tr.setLang(l);
  }

  logout() { this.auth.logout(); }

  get initials() {
    const name = this.user?.name ?? '';
    return name.trim().slice(0, 2).toUpperCase() || 'U';
  }
}
