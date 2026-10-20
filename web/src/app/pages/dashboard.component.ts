import { Component, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService, AuditEvent, Bill, Hearing, Summary } from '../core/api.service';
import { AuthService, SearchService } from '../core/auth.service';

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink],
  templateUrl: './dashboard.component.html',
})
export class DashboardComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  private readonly search = inject(SearchService);
  readonly summary = signal<Summary | null>(null);
  readonly bills = signal<Bill[]>([]);
  readonly hearing = signal<Hearing | null>(null);
  readonly activity = signal<AuditEvent[]>([]);
  readonly bars = signal<{ label: string; referred: number; agenda: number; reported: number }[]>([]);

  constructor() {
    effect(() => {
      const committeeId = this.auth.committeeId();
      const query = this.search.query();
      if (!committeeId) {
        return;
      }
      this.api.summary().subscribe((summary) => {
        this.summary.set(summary);
        this.bars.set(this.buildBars(summary));
      });
      this.api.bills(undefined, query).subscribe((bills) => this.bills.set(bills.slice(0, 4)));
      this.api.hearings().subscribe((hearings) => {
        const upcoming = hearings.find((item) => new Date(item.startsAt).getTime() >= Date.now());
        this.hearing.set(upcoming ?? null);
      });
      this.api.audit().subscribe((events) => this.activity.set(events.slice(0, 5)));
    });
  }

  reported(): number {
    const summary = this.summary();
    return (summary?.reportedFavorably ?? 0) + (summary?.reportedUnfavorably ?? 0);
  }

  mixWidth(part: number): string {
    const summary = this.summary();
    const total = (summary?.referred ?? 0) + (summary?.onAgenda ?? 0) + this.reported() + (summary?.temporarilyPostponed ?? 0);
    if (!total) {
      return '0%';
    }
    return `${(part / total) * 100}%`;
  }

  barHeight(value: number): string {
    const max = Math.max(1, ...this.bars().map((bar) => bar.referred + bar.agenda + bar.reported));
    return `${Math.max(4, Math.round(((value || 0) / max) * 72))}px`;
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

  statusLabel(status: string): string {
    return status.replaceAll('_', ' ');
  }

  private buildBars(summary: Summary) {
    const days: { label: string; key: string; referred: number; agenda: number; reported: number }[] = [];
    const today = new Date();
    for (let offset = 6; offset >= 0; offset -= 1) {
      const date = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), today.getUTCDate() - offset));
      const key = date.toISOString().slice(0, 10);
      days.push({
        label: date.toLocaleDateString('en-US', { weekday: 'short', timeZone: 'UTC' }),
        key,
        referred: 0,
        agenda: 0,
        reported: 0,
      });
    }
    for (const bucket of summary.days) {
      const day = days.find((item) => item.key === bucket.day.slice(0, 10));
      if (!day) {
        continue;
      }
      if (bucket.action === 'bill.placed_on_agenda') {
        day.agenda += bucket.count;
      } else if (bucket.action.startsWith('bill.reported')) {
        day.reported += bucket.count;
      } else {
        day.referred += bucket.count;
      }
    }
    return days;
  }
}
