import { Routes } from '@angular/router';
import { anonymousOnlyGuard, authGuard } from 'auth';
import { NAV, navChild } from './shell/navigation';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [anonymousOnlyGuard],
    loadComponent: () => import('./auth/login').then((m) => m.Login),
  },
  {
    // Everything signed-in lives inside the shell, so the toolbar and navigation are laid out
    // once rather than repeated by each screen.
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./shell/shell').then((m) => m.Shell),
    children: [
      navChild(NAV.overview, () => import('./overview/overview').then((m) => m.Overview)),
      navChild(NAV.orders, () => import('./orders/queue').then((m) => m.Queue)),
      navChild(NAV.history, () => import('./orders/history').then((m) => m.History)),
      navChild(NAV.menu, () => import('./menu/menu').then((m) => m.Menu)),
      navChild(NAV.settings, () => import('./settings/settings').then((m) => m.Settings)),
      navChild(NAV.platform, () => import('./platform/platform').then((m) => m.Platform)),
      {
        // Where roleGuard sends a signed-in user who lacks the role. Inside the shell, so they
        // keep the navigation and can go somewhere they are allowed; a bare page would strand
        // them. It carries no roleGuard of its own, which would be a loop.
        path: 'forbidden',
        loadComponent: () => import('./auth/forbidden').then((m) => m.Forbidden),
      },
      { path: '**', redirectTo: '' },
    ],
  },
];
