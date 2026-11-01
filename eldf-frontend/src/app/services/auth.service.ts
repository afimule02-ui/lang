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
  private readonly API = '/api/auth';
  user = signal<User | null>(null);
  lang = signal<'am' | 'en'>('am');
  readonly ready: Promise<void>;

  constructor(private http: HttpClient, private router: Router) {
    this.ready = this.restoreSession();
  }

  private async restoreSession(): Promise<void> {
    try {
      const res: any = await firstValueFrom(this.http.get(`${this.API}/me`, { withCredentials: true }));
      this.user.set(res.user);
      this.lang.set(res.user.language === 'en' ? 'en' : 'am');
    } catch {}
  }

  async register(data: FormData): Promise<User> {
    const res: any = await firstValueFrom(this.http.post(`${this.API}/register`, data, { withCredentials: true }));
    return res.user;
  }

  async login(data: FormData): Promise<User> {
    const res: any = await firstValueFrom(this.http.post(`${this.API}/login`, data, { withCredentials: true }));
    this.user.set(res.user);
    this.lang.set(res.user.language === 'en' ? 'en' : 'am');
    return res.user;
  }

  async logout(navigate = true) {
    await firstValueFrom(this.http.post(`${this.API}/logout`, {}, { withCredentials: true })).catch(() => {});
    this.user.set(null);
    if (navigate) this.router.navigate(['/']);
  }

  setLang(l: 'am' | 'en') {
    this.lang.set(l);
  }
}
