import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class ApiService {
  readonly API = 'http://localhost:8001';

  constructor(private http: HttpClient) {}

  async saveSpeakers(speakers: any[]) {
    return firstValueFrom(this.http.post(`${this.API}/api/speakers`, { speakers }, { withCredentials: true }));
  }

  async submitAudio(form: FormData) {
    return firstValueFrom(this.http.post(`${this.API}/api/submissions`, form, { withCredentials: true }));
  }

  async deleteSubmission(id: number) {
    return firstValueFrom(this.http.delete(`${this.API}/api/submissions/${id}`, { withCredentials: true }));
  }

  async processAudio(form: FormData) {
    return firstValueFrom(this.http.post(`${this.API}/api/audio/process`, form, {
      withCredentials: true,
      responseType: 'blob',
      observe: 'response'
    }));
  }

  health() {
    return firstValueFrom(this.http.get(`${this.API}/health`));
  }
}
