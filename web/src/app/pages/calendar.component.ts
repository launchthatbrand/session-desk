import { Component, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService, Hearing } from '../core/api.service';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-calendar',
  imports: [ReactiveFormsModule],
  templateUrl: './calendar.component.html',
})
export class CalendarComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  private readonly forms = inject(FormBuilder);
  readonly hearings = signal<Hearing[]>([]);
  readonly open = signal(false);
  readonly error = signal('');
  readonly form = this.forms.nonNullable.group({
    startsAt: ['', Validators.required],
    room: ['', Validators.required],
  });

  constructor() {
    effect(() => {
      if (!this.auth.committeeId()) {
        return;
      }
      this.load();
    });
  }

  load(): void {
    this.api.hearings().subscribe((hearings) => this.hearings.set(hearings));
  }

  when(value: string): string {
    return new Intl.DateTimeFormat('en-US', {
      timeZone: 'America/New_York',
      weekday: 'long',
      month: 'short',
      day: 'numeric',
      hour: 'numeric',
      minute: '2-digit',
    }).format(new Date(value));
  }

  save(): void {
    const value = this.form.getRawValue();
    if (this.form.invalid) {
      this.error.set('Date and room are required.');
      return;
    }
    this.api.scheduleHearing(new Date(value.startsAt).toISOString(), value.room).subscribe({
      next: () => {
        this.open.set(false);
        this.load();
      },
      error: () => this.error.set('Only a clerk can schedule a hearing.'),
    });
  }
}
