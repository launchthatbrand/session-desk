import { Injectable, computed, signal } from '@angular/core';

export interface SessionUser {
  id: string;
  email: string;
  displayName: string;
}

export interface SessionCommittee {
  id: string;
  name: string;
  role: 'clerk' | 'analyst' | 'viewer';
}

interface StoredSession {
  token: string;
  user: SessionUser;
  committees: SessionCommittee[];
  committeeId: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly storageKey = 'session-desk';
  readonly token = signal<string | null>(null);
  readonly user = signal<SessionUser | null>(null);
  readonly committees = signal<SessionCommittee[]>([]);
  readonly committeeId = signal<string | null>(null);
  readonly role = computed(() => this.committees().find((item) => item.id === this.committeeId())?.role ?? 'viewer');
  readonly committeeName = computed(() => this.committees().find((item) => item.id === this.committeeId())?.name ?? '');

  constructor() {
    const raw = sessionStorage.getItem(this.storageKey);
    if (!raw) {
      return;
    }
    const stored = JSON.parse(raw) as StoredSession;
    this.token.set(stored.token);
    this.user.set(stored.user);
    this.committees.set(stored.committees);
    this.committeeId.set(stored.committeeId);
  }

  setSession(token: string, user: SessionUser, committees: SessionCommittee[]): void {
    this.token.set(token);
    this.user.set(user);
    this.committees.set(committees);
    this.committeeId.set(committees[0]?.id ?? null);
    this.persist();
  }

  selectCommittee(id: string): void {
    this.committeeId.set(id);
    this.persist();
  }

  clear(): void {
    this.token.set(null);
    this.user.set(null);
    this.committees.set([]);
    this.committeeId.set(null);
    sessionStorage.removeItem(this.storageKey);
  }

  private persist(): void {
    const token = this.token();
    const user = this.user();
    const committeeId = this.committeeId();
    if (!token || !user || !committeeId) {
      return;
    }
    const stored: StoredSession = { token, user, committees: this.committees(), committeeId };
    sessionStorage.setItem(this.storageKey, JSON.stringify(stored));
  }
}

@Injectable({ providedIn: 'root' })
export class SearchService {
  readonly query = signal('');
}
