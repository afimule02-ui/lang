import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom, timeout } from 'rxjs';

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

  updateAdminProfile(profile: { name: string; phone: string; currentPassword: string }) {
    return firstValueFrom(this.http.patch<{ user: { id: number; name: string; phone: string; role: 'admin'; language: string } }>(`${this.API}/auth/admin-profile`, {
      fullName: profile.name, phone: profile.phone, currentPassword: profile.currentPassword
    }, { withCredentials: true }));
  }

  reassignDraft(id: number, assignedUserId: number | null) {
    return firstValueFrom(this.http.patch(`${this.API}/admin/drafts/${id}/assignment`, { assignedUserId }, { withCredentials: true }));
  }

  assignSubmissionReview(id: number, agentId: number | null) {
    return firstValueFrom(this.http.patch(`${this.API}/admin/submissions/${id}/assignment`, { agentId }, { withCredentials: true }));
  }

  getDraft<T = Record<string, any>>(id: number) {
    return firstValueFrom(this.http.get<T>(`${this.API}/drafts/${id}`, { withCredentials: true }));
  }

  getDraftAudio(id: number) {
    return firstValueFrom(this.http.get(`${this.API}/drafts/${id}/audio`, { withCredentials: true, responseType: 'blob' }));
  }

  saveDraft(form: FormData) {
    return firstValueFrom(this.http.post<{ id: number }>(`${this.API}/drafts`, form, { withCredentials: true }).pipe(timeout({ first: 30_000 })));
  }

  deleteDraft(id: number) {
    return firstValueFrom(this.http.delete(`${this.API}/drafts/${id}`, { withCredentials: true }));
  }

  requestPayment(amount: number) {
    return firstValueFrom(this.http.post(`${this.API}/payments/requests`, { amount }, { withCredentials: true }));
  }

  createSupportTicket(subject: string, message: string) {
    return firstValueFrom(this.http.post(`${this.API}/support/tickets`, { subject, message }, { withCredentials: true }));
  }

  sendSupportMessage(id: number, message: string) {
    return firstValueFrom(this.http.post(`${this.API}/support/tickets/${id}/messages`, { message }, { withCredentials: true }));
  }

  updateSupportTicket(id: number, update: { status: string; assignedAgentId?: number }) {
    return firstValueFrom(this.http.patch(`${this.API}/support/tickets/${id}`, update, { withCredentials: true }));
  }

  escalateSupportTicket(id: number) {
    return firstValueFrom(this.http.patch(`${this.API}/support/tickets/${id}`, { escalate: true }, { withCredentials: true }));
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
    return firstValueFrom(this.http.post(`${this.API}/speakers`, { speakers }, { withCredentials: true }).pipe(
      timeout({ first: 30_000 })
    ));
  }

  async getSpeakers<T = any[]>() {
    return firstValueFrom(this.http.get<T>(`${this.API}/speakers`, { withCredentials: true }));
  }

  withdrawSpeakerConsent(speakerId: string) {
    return firstValueFrom(this.http.post(`${this.API}/speakers/${encodeURIComponent(speakerId)}/consent/withdrawal`, {}, { withCredentials: true }));
  }

  speakerConsents<T = any[]>() {
    return firstValueFrom(this.http.get<T>(`${this.API}/admin/speaker-consents`, { withCredentials: true }));
  }

  async submitAudio(form: FormData) {
    return firstValueFrom(this.http.post<{ ok: boolean; submissionId: number; status: string }>(`${this.API}/submissions`, form, { withCredentials: true }).pipe(timeout({ first: 15 * 60 * 1000 })));
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
