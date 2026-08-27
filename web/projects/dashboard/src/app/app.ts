import { Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { firstValueFrom } from 'rxjs';

interface RestaurantSummary {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  minOrderUsd: number;
  defaultPrepMinutes: number;
  isAcceptingOrders: boolean;
  isOpenNow: boolean;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

/**
 * A deliberately small first screen. Its job is not to be the dashboard — it is to prove the
 * whole chain works end to end before anything is built on top: Angular serves, the dev proxy
 * reaches the API, the API reaches SQL Server, and the seeded data comes back.
 *
 * Replaced by the real shell in step 7.
 */
@Component({
  imports: [MatToolbarModule, MatCardModule, MatChipsModule, MatIconModule, MatProgressBarModule],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  private readonly http = inject(HttpClient);

  protected readonly restaurants = signal<RestaurantSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      const page = await firstValueFrom(
        this.http.get<PagedResult<RestaurantSummary>>('/api/restaurants'),
      );
      this.restaurants.set(page.items);
    } catch {
      this.error.set('Could not reach the API. Is it running on port 5080?');
    } finally {
      this.loading.set(false);
    }
  }
}
