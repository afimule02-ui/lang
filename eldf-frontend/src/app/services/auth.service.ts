import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

export interface User {
  id: number;
  name: string;
  phone: string;
  role: 'contributor' | 'agent' | 'admin';
  language: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  readonly API = 'http://localhost:8001';
  user = signal<User | null>(null);
  lang = signal<'am' | 'en'>('am');

  constructor(private http: HttpClient, private router: Router) {
    this.restoreSession();
  }

  private async restoreSession() {
    try {
      const res: any = await firstValueFrom(this.http.get(`${this.API}/api/auth/me`, { withCredentials: true }));
      this.user.set(res.user);
      this.lang.set(res.user.language === 'en' ? 'en' : 'am');
    } catch {}
  }

  async register(data: FormData): Promise<User> {
    const res: any = await firstValueFrom(this.http.post(`${this.API}/api/auth/register`, data, { withCredentials: true }));
    this.user.set(res.user);
    this.lang.set(res.user.language === 'en' ? 'en' : 'am');
    return res.user;
  }

  async login(data: FormData): Promise<User> {
    const res: any = await firstValueFrom(this.http.post(`${this.API}/api/auth/login`, data, { withCredentials: true }));
    this.user.set(res.user);
    this.lang.set(res.user.language === 'en' ? 'en' : 'am');
    return res.user;
  }

  async logout() {
    await firstValueFrom(this.http.post(`${this.API}/api/auth/logout`, {}, { withCredentials: true })).catch(() => {});
    this.user.set(null);
    this.router.navigate(['/']);
  }

  setLang(l: 'am' | 'en') {
    this.lang.set(l);
  }
}
