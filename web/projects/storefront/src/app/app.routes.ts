import { Routes } from '@angular/router';
import { anonymousOnlyGuard, authGuard } from 'auth';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./catalog/catalog').then((m) => m.Catalog),
  },
  {
    // By slug rather than id: this is the link somebody sends a friend, and an id in it would
    // make that link unreadable and unguessable at once.
    path: 'restaurants/:slug',
    loadComponent: () => import('./restaurant/restaurant').then((m) => m.Restaurant),
  },

  // ---------------------------------------------------------------- being somebody
  //
  // Three of these turn a signed-in visitor away, because a sign-in form that immediately
  // bounces reads as a broken application. The fourth deliberately does not: a reset link is
  // followed by somebody who may well still be signed in on this browser — that is exactly the
  // case where they have forgotten the password and want a new one.
  {
    path: 'login',
    canActivate: [anonymousOnlyGuard],
    loadComponent: () => import('./account/sign-in').then((m) => m.SignIn),
  },
  {
    path: 'register',
    canActivate: [anonymousOnlyGuard],
    loadComponent: () => import('./account/register').then((m) => m.Register),
  },
  {
    path: 'forgot-password',
    canActivate: [anonymousOnlyGuard],
    loadComponent: () => import('./account/forgot-password').then((m) => m.ForgotPassword),
  },
  {
    // From the shared library, in the same words the dashboard shows an invited owner.
    path: 'reset-password',
    loadComponent: () => import('ui').then((m) => m.ResetPassword),
  },
  {
    path: 'account',
    canActivate: [authGuard],
    loadComponent: () => import('./account/account').then((m) => m.Account),
  },

  { path: '**', redirectTo: '' },
];
