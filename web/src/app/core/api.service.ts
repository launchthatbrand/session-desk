import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

export interface Bill {
  id: string;
  number: string;
  title: string;
  sponsor: string;
  status: string;
  lastAction: string;
  updatedAt: string;
}

export interface DayBucket {
  day: string;
  action: string;
  count: number;
}

export interface Summary {
  referred: number;
  onAgenda: number;
  reportedFavorably: number;
  reportedUnfavorably: number;
  temporarilyPostponed: number;
  days: DayBucket[];
}

export interface HearingBill {
  id: string;
  number: string;
  title: string;
}

export interface Hearing {
  id: string;
  startsAt: string;
  room: string;
  bills: HearingBill[];
}

export interface AuditEvent {
  id: string;
  billNumber: string | null;
  detail: string;
  actor: string;
  action: string;
  traceId: string | null;
  createdAt: string;
}

export interface Brief {
  briefingId: string;
  draft: string;
  model: string;
  promptVersion: string;
}

export interface LoginResult {
  token: string;
  user: { id: string; email: string; displayName: string };
  committees: { id: string; name: string; role: 'clerk' | 'analyst' | 'viewer' }[];
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  login(email: string, password: string) {
    return this.http.post<LoginResult>('/api/auth/login', { email, password });
  }

  bills(status?: string, q?: string) {
    let params = new HttpParams();
    if (status) {
      params = params.set('status', status);
    }
    if (q) {
      params = params.set('q', q);
    }
    return this.http.get<Bill[]>('/api/bills', { params });
  }

  summary() {
    return this.http.get<Summary>('/api/bills/summary');
  }

  recordAction(id: string, body: { action: string; hearingStartsAt?: string; room?: string; note?: string }) {
    return this.http.post<Bill>(`/api/bills/${id}/actions`, body);
  }

  brief(id: string) {
    return this.http.post<Brief>(`/api/bills/${id}/brief`, {});
  }

  decideBrief(id: string, briefingId: string, decision: 'accept' | 'discard', note?: string) {
    return this.http.post<{ briefingId: string }>(`/api/bills/${id}/brief/${briefingId}`, { decision, note });
  }

  hearings() {
    return this.http.get<Hearing[]>('/api/hearings');
  }

  scheduleHearing(startsAt: string, room: string) {
    return this.http.post<Hearing>('/api/hearings', { startsAt, room });
  }

  audit() {
    return this.http.get<AuditEvent[]>('/api/audit');
  }
}
