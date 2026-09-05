import { Injectable, computed, inject, signal } from '@angular/core';
import { PlatformRestaurantResponse, PlatformRestaurantsClient, describeError } from 'api-client';
import { firstValueFrom } from 'rxjs';

/**
 * Every restaurant on the platform, and the two things the platform sets about each.
 *
 * <h4>Reloaded after every change</h4>
 *
 * Same reason as the staff list: the response to one change is not the only thing it changes. The
 * live-order count moves on its own while somebody is looking at this screen, and a stale one
 * next to a switch that hides a restaurant is the wrong number to be reading.
 */
@Injectable()
export class PlatformStore {
  private readonly client = inject(PlatformRestaurantsClient);

  private readonly rowsSignal = signal<PlatformRestaurantResponse[]>([]);

  readonly rows = this.rowsSignal.asReadonly();
  readonly loading = signal(true);
  readonly loaded = signal(false);
  readonly error = signal<string | null>(null);

  /** Which restaurant is mid-request, so only its own controls are disabled. */
  readonly busy = signal<string | null>(null);

  readonly hiddenCount = computed(() => this.rowsSignal().filter((r) => !r.isActive).length);

  async load(): Promise<void> {
    this.loading.set(true);

    try {
      this.rowsSignal.set(await firstValueFrom(this.client.list()));
      this.error.set(null);
      this.loaded.set(true);
    } catch (error) {
      this.error.set(describeError(error, 'Could not load the platform list.'));
    } finally {
      this.loading.set(false);
    }
  }

  async setCommission(
    row: PlatformRestaurantResponse,
    commissionPercent: number,
  ): Promise<boolean> {
    return this.change(
      row,
      () => firstValueFrom(this.client.setCommission(row.id, { commissionPercent })),
      `Could not change what ${row.name} is charged.`,
    );
  }

  async setListing(row: PlatformRestaurantResponse, isActive: boolean): Promise<boolean> {
    return this.change(
      row,
      () => firstValueFrom(this.client.setListing(row.id, { isActive })),
      `Could not ${isActive ? 'list' : 'hide'} ${row.name}.`,
    );
  }

  private async change(
    row: PlatformRestaurantResponse,
    request: () => Promise<unknown>,
    whenItFails: string,
  ): Promise<boolean> {
    this.busy.set(row.id);
    this.error.set(null);

    try {
      await request();
      await this.load();
      return true;
    } catch (error) {
      // Named, because a list of restaurants with one failure needs to say which one.
      this.error.set(describeError(error, whenItFails));
      return false;
    } finally {
      this.busy.set(null);
    }
  }
}
