import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule],
  templateUrl: './login.component.html',
})
export class LoginComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly forms = inject(FormBuilder);
  readonly error = signal('');
  readonly form = this.forms.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  constructor() {
    if (this.auth.token()) {
      void this.router.navigateByUrl('/');
    }
  }

  submit(): void {
    if (this.form.invalid) {
      this.error.set('Email and password are required.');
      return;
    }
    const { email, password } = this.form.getRawValue();
    this.api.login(email, password).subscribe({
      next: (result) => {
        this.auth.setSession(result.token, result.user, result.committees);
        void this.router.navigateByUrl('/');
      },
      error: () => this.error.set('Email or password is incorrect.'),
    });
  }
}
