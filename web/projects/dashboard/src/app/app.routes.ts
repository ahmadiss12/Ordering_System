import { Routes } from '@angular/router';
import { anonymousOnlyGuard, authGuard } from 'auth';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [anonymousOnlyGuard],
    loadComponent: () => import('./auth/login').then((m) => m.Login),
  },
  {
    // No guard: this is where roleGuard sends a signed-in user who lacks the role. Guarding it
    // with authGuard as well would be a redirect loop waiting to happen.
    path: 'forbidden',
    loadComponent: () => import('./auth/forbidden').then((m) => m.Forbidden),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./home/home').then((m) => m.Home),
  },
  { path: '**', redirectTo: '' },
];
