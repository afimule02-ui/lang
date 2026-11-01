import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class ApiService {
  // Keep API requests on the current origin in production. Angular's dev proxy
  // forwards these relative paths to the local ASP.NET Core server.
  private readonly API = '/api';

  constructor(private http: HttpClient) {}

  workspace<T = Record<string, any>>() {
    return firstValueFrom(this.http.get<T>(`${this.API}/workspace`));
  }

  agentReview(id: number, decision: 'accept' | 'return', comment = '') {
    return firstValueFrom(this.http.post(`${this.API}/submissions/${id}/agent-review`, { decision, comment }));
  }

  adminReview(id: number, decision: 'approve' | 'return' | 'reject', comment = '', reason = '', awardAmount?: number) {
    return firstValueFrom(this.http.post(`${this.API}/submissions/${id}/admin-review`, {
      decision, comment, reason, awardAmount, currency: 'ETB'
    }));
  }

  updateAdminUser(id: number, change: { name: string; role: string; active: boolean; agentId?: number | null }) {
    return firstValueFrom(this.http.patch(`${this.API}/admin/users/${id}`, {
      fullName: change.name, role: change.role, isActive: change.active,
      agentId: change.role === 'contributor' ? change.agentId ?? null : null,
      clearAgentAssignment: change.role !== 'contributor' || !change.agentId
    }));
  }

  requestPayment(amount: number) {
    return firstValueFrom(this.http.post(`${this.API}/payments/requests`, { amount }, { withCredentials: true }));
  }

  requestPasswordReset(phone: string) {
    return firstValueFrom(this.http.post(`${this.API}/auth/password-reset/request`, { phone }));
  }

  resetUserPassword(id: number) {
    return firstValueFrom(this.http.post<{ temporaryPassword: string }>(`${this.API}/admin/users/${id}/password-reset`, {}, { withCredentials: true }));
  }

  changePassword(currentPassword: string, newPassword: string) {
    return firstValueFrom(this.http.post(`${this.API}/auth/change-password`, { currentPassword, newPassword }, { withCredentials: true }));
  }

  reviewPaymentAsAgent(id: number, decision: 'accept' | 'return', note: string) {
    return firstValueFrom(this.http.post(`${this.API}/payments/requests/${id}/agent-review`, { decision, note }, { withCredentials: true }));
  }

  reviewPaymentAsAdmin(id: number, decision: 'approve' | 'reject' | 'paid', note: string, proofReference = '') {
    return firstValueFrom(this.http.post(`${this.API}/admin/payments/${id}/review`, { decision, note, proofReference }, { withCredentials: true }));
  }

  async saveSpeakers(speakers: any[]) {
    return firstValueFrom(this.http.post(`${this.API}/speakers`, { speakers }, { withCredentials: true }));
  }

  async submitAudio(form: FormData) {
    return firstValueFrom(this.http.post(`${this.API}/submissions`, form, { withCredentials: true }));
  }

  async deleteSubmission(id: number) {
    return firstValueFrom(this.http.delete(`${this.API}/submissions/${id}`, { withCredentials: true }));
  }

  async processAudio(form: FormData) {
    return firstValueFrom(this.http.post(`${this.API}/audio/process`, form, {
      withCredentials: true,
      responseType: 'blob',
      observe: 'response'
    }));
  }

  health() {
    return firstValueFrom(this.http.get('/health'));
  }
}
