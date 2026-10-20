import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const token = auth.token();
  const committeeId = auth.committeeId();
  const isLogin = request.url.includes('/api/auth/login');
  let headers = request.headers;
  if (token && !isLogin) {
    headers = headers.set('Authorization', `Bearer ${token}`);
  }
  if (committeeId && !isLogin) {
    headers = headers.set('X-Committee-Id', committeeId);
  }
  return next(request.clone({ headers })).pipe(
    catchError((error) => {
      if (error.status === 401 && !isLogin) {
        auth.clear();
        void router.navigateByUrl('/login');
      }
      return throwError(() => error);
    }),
  );
};
