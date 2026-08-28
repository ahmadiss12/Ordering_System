import { Component, inject, signal } from '@angular/core';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RestaurantSummary, RestaurantsClient } from 'api-client';
import { firstValueFrom } from 'rxjs';

/**
 * A deliberately small first screen. Its job is not to be the dashboard — it is to prove the
 * chain works before anything is built on it: Angular serves, the generated client calls the
 * API through the dev proxy, the API reaches SQL Server, and real rows come back.
 *
 * Note what is absent: no URL string, and no hand-written interface for the response. Both come
 * from the generated client, so a change to either on the server is a compile error here rather
 * than an `undefined` somebody finds later. Replaced by the real shell in step 7.
 */
@Component({
  imports: [MatToolbarModule, MatCardModule, MatChipsModule, MatIconModule, MatProgressBarModule],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  private readonly restaurantsClient = inject(RestaurantsClient);

  protected readonly restaurants = signal<RestaurantSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      const page = await firstValueFrom(this.restaurantsClient.list());
      this.restaurants.set(page.items ?? []);
    } catch {
      this.error.set('Could not reach the API. Is it running on port 5080?');
    } finally {
      this.loading.set(false);
    }
  }
}
