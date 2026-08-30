import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { RestaurantSummary, RestaurantsClient } from 'api-client';
import { AuthService } from 'auth';
import { firstValueFrom } from 'rxjs';

/**
 * The landing section: the restaurant this account works for, and its current state.
 *
 * It filters the public listing by the `restaurant_id` claim rather than calling something like
 * `GET /api/restaurant`, because that endpoint does not exist yet. Showing an owner all three
 * restaurants on the platform would be worse than one redundant field in the response, so this
 * is the trade until there is a scoped endpoint to call.
 */
@Component({
  selector: 'app-overview',
  imports: [
    RouterLink,
    MatCardModule,
    MatChipsModule,
    MatIconModule,
    MatProgressBarModule,
    MatButtonModule,
  ],
  templateUrl: './overview.html',
  styleUrl: './overview.scss',
})
export class Overview {
  private readonly restaurantsClient = inject(RestaurantsClient);
  private readonly auth = inject(AuthService);

  protected readonly session = this.auth.session;
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  private readonly all = signal<RestaurantSummary[]>([]);

  /**
   * What this account is responsible for. A platform admin has no `restaurant_id` and is not
   * scoped to one, so they see the whole platform.
   */
  protected readonly restaurants = computed(() => {
    const scope = this.session.restaurantId();
    return scope ? this.all().filter((r) => r.id === scope) : this.all();
  });

  protected readonly isScoped = computed(() => this.session.restaurantId() !== null);

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      const page = await firstValueFrom(this.restaurantsClient.list());
      this.all.set(page.items ?? []);
    } catch {
      this.error.set('Could not reach the API. Is it running on port 5080?');
    } finally {
      this.loading.set(false);
    }
  }
}
