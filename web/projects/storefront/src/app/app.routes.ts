import { Routes } from '@angular/router';

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
  { path: '**', redirectTo: '' },
];
