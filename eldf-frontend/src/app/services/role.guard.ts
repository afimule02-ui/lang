import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const roleGuard: CanActivateFn = async route => {
  const auth = inject(AuthService);
  const router = inject(Router);
  await auth.ready;
  const role = auth.user()?.role ?? null;
  const allowed = route.data['roles'] as string[] | undefined;
  if (role && (!allowed || allowed.includes(role))) return true;
  return router.createUrlTree(['/login', role ?? 'contributor']);
};
