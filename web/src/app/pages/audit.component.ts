import { Component, effect, inject, signal } from '@angular/core';
import { ApiService, AuditEvent } from '../core/api.service';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-audit',
  templateUrl: './audit.component.html',
})
export class AuditComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  readonly events = signal<AuditEvent[]>([]);

  constructor() {
    effect(() => {
      if (!this.auth.committeeId()) {
        return;
      }
      this.api.audit().subscribe((events) => this.events.set(events));
    });
  }

  when(value: string): string {
    return new Intl.DateTimeFormat('en-US', {
      timeZone: 'America/New_York',
      month: 'short',
      day: 'numeric',
      hour: 'numeric',
      minute: '2-digit',
    }).format(new Date(value));
  }
}
