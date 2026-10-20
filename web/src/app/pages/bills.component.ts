import { Component, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService, Bill } from '../core/api.service';
import { AuthService, SearchService } from '../core/auth.service';

@Component({
  selector: 'app-bills',
  imports: [ReactiveFormsModule],
  templateUrl: './bills.component.html',
})
export class BillsComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  private readonly search = inject(SearchService);
  private readonly forms = inject(FormBuilder);
  readonly bills = signal<Bill[]>([]);
  readonly filter = signal('');
  readonly open = signal(false);
  readonly error = signal('');
  private readonly reload = signal(0);
  readonly form = this.forms.nonNullable.group({
    billId: ['', Validators.required],
    action: ['bill.placed_on_agenda'],
    hearingStartsAt: [''],
    room: [''],
    note: [''],
  });

  constructor() {
    effect(() => {
      const committeeId = this.auth.committeeId();
      const query = this.search.query();
      const status = this.filter();
      this.reload();
      if (!committeeId) {
        return;
      }
      this.api.bills(status || undefined, query || undefined).subscribe((bills) => this.bills.set(bills));
    });
  }

  setFilter(status: string): void {
    this.filter.set(status);
  }

  canWrite(): boolean {
    return this.auth.role() !== 'viewer';
  }

  openDialog(bill?: Bill): void {
    this.error.set('');
    this.form.reset({
      billId: bill?.id ?? this.bills()[0]?.id ?? '',
      action: this.auth.role() === 'analyst' ? 'bill.note_added' : 'bill.placed_on_agenda',
      hearingStartsAt: '',
      room: '',
      note: '',
    });
    this.open.set(true);
  }

  close(): void {
    this.open.set(false);
  }

  save(): void {
    const value = this.form.getRawValue();
    if (!value.billId) {
      this.error.set('Choose a bill.');
      return;
    }
    const action = this.auth.role() === 'analyst' ? 'bill.note_added' : value.action;
    this.api.recordAction(value.billId, {
      action,
      hearingStartsAt: value.hearingStartsAt ? new Date(value.hearingStartsAt).toISOString() : undefined,
      room: value.room || undefined,
      note: value.note || undefined,
    }).subscribe({
      next: () => {
        this.open.set(false);
        this.reload.update((value) => value + 1);
      },
      error: () => this.error.set('The desk could not record that action.'),
    });
  }

  statusLabel(status: string): string {
    return status.replaceAll('_', ' ');
  }
}
