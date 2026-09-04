import { Injectable, computed, inject, signal } from '@angular/core';
import {
  RestaurantSettingsClient,
  RestaurantSettingsResponse,
  UpdateRestaurantSettingsRequest,
  describeError,
} from 'api-client';
import { firstValueFrom } from 'rxjs';

/**
 * The restaurant's own settings.
 *
 * Two write paths rather than one, mirroring the API. Saving the profile is an owner submitting a
 * form; pausing orders is a cook pressing one switch mid-service, and putting the second inside
 * the first would make the fastest action on the screen the slowest — the same reasoning that
 * kept availability out of the item dialog in Phase 2.
 */
@Injectable()
export class SettingsStore {
  private readonly client = inject(RestaurantSettingsClient);

  private readonly settingsSignal = signal<RestaurantSettingsResponse | null>(null);

  readonly settings = this.settingsSignal.asReadonly();
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  /** Set after a successful save, so the screen can say so rather than looking inert. */
  readonly saved = signal(false);

  readonly isPaused = computed(() => this.settingsSignal()?.isAcceptingOrders === false);

  async load(): Promise<void> {
    this.loading.set(true);

    try {
      this.settingsSignal.set(await firstValueFrom(this.client.get()));
      this.error.set(null);
    } catch (error) {
      this.error.set(describeError(error, 'Could not load your settings.'));
    } finally {
      this.loading.set(false);
    }
  }

  async save(request: UpdateRestaurantSettingsRequest): Promise<boolean> {
    return this.write('Could not save your settings.', async () => {
      this.settingsSignal.set(await firstValueFrom(this.client.update(request)));
      this.saved.set(true);
    });
  }

  async setAcceptingOrders(isAcceptingOrders: boolean): Promise<boolean> {
    // No "saved" flourish here. The switch shows its own new position, which is the whole
    // feedback a cook wants at eight on a Friday.
    return this.write('Could not change whether you are taking orders.', async () => {
      this.settingsSignal.set(
        await firstValueFrom(this.client.setAcceptingOrders({ isAcceptingOrders })),
      );
    });
  }

  private async write(fallback: string, action: () => Promise<void>): Promise<boolean> {
    this.saving.set(true);
    this.error.set(null);
    this.saved.set(false);

    try {
      await action();
      return true;
    } catch (error) {
      this.error.set(describeError(error, fallback));
      return false;
    } finally {
      this.saving.set(false);
    }
  }
}
