import { Component, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { ApiService, Bill, Brief } from '../core/api.service';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-bills',
  imports: [ReactiveFormsModule],
  templateUrl: './bills.component.html',
})
export class BillsComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly forms = inject(FormBuilder);
  readonly bills = signal<Bill[]>([]);
  readonly filter = signal('');
  readonly open = signal(false);
  readonly error = signal('');
  readonly brief = signal<Brief | null>(null);
  readonly drafting = signal(false);
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
      const status = this.filter();
      this.reload();
      if (!committeeId) {
        return;
      }
      this.api.bills(status || undefined).subscribe((bills) => {
        this.bills.set(bills);
        const openId = this.route.snapshot.queryParamMap.get('bill');
        const match = bills.find((bill) => bill.id === openId);
        if (match) {
          this.openDialog(match);
        }
      });
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
    this.brief.set(null);
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

  draftBrief(): void {
    const billId = this.form.controls.billId.value;
    if (!billId) {
      this.error.set('Choose a bill.');
      return;
    }
    this.error.set('');
    this.drafting.set(true);
    this.api.brief(billId).subscribe({
      next: (brief) => {
        this.brief.set(brief);
        this.form.controls.note.setValue(brief.draft);
        this.drafting.set(false);
      },
      error: (error) => {
        this.brief.set(null);
        this.drafting.set(false);
        const message = error?.error?.error;
        this.error.set(typeof message === 'string' ? message : 'The desk could not draft that brief.');
      },
    });
  }

  acceptBrief(): void {
    const current = this.brief();
    const billId = this.form.controls.billId.value;
    if (!current || !billId) {
      return;
    }
    this.api.decideBrief(billId, current.briefingId, 'accept', this.form.controls.note.value).subscribe({
      next: () => {
        this.open.set(false);
        this.reload.update((value) => value + 1);
      },
      error: () => this.error.set('The desk could not accept that brief.'),
    });
  }

  discardBrief(): void {
    const current = this.brief();
    const billId = this.form.controls.billId.value;
    if (!current || !billId) {
      return;
    }
    this.api.decideBrief(billId, current.briefingId, 'discard').subscribe({
      next: () => {
        this.brief.set(null);
        this.form.controls.note.setValue('');
      },
      error: () => this.error.set('The desk could not discard that brief.'),
    });
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
