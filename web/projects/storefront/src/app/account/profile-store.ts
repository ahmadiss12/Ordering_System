import { Injectable, inject, signal } from '@angular/core';
import { MeClient, ProfileResponse, describeError } from 'api-client';
import { firstValueFrom } from 'rxjs';

/**
 * The signed-in person's own details.
 *
 * <p>
 * Read from the server rather than decoded from the token they are holding. The token was minted
 * when they signed in, so a name corrected since is not in it — and a screen that showed the old
 * one straight after a save would look as though the save had failed.
 * </p>
 */
@Injectable()
export class ProfileStore {
  private readonly client = inject(MeClient);

  private readonly profileSignal = signal<ProfileResponse | null>(null);

  readonly profile = this.profileSignal.asReadonly();
  readonly loading = signal(true);
  readonly loaded = signal(false);
  readonly error = signal<string | null>(null);

  readonly saving = signal(false);
  readonly saved = signal(false);

  readonly changingPassword = signal(false);
  readonly passwordChanged = signal(false);
  readonly passwordError = signal<string | null>(null);

  async load(): Promise<void> {
    this.loading.set(true);

    try {
      this.profileSignal.set(await firstValueFrom(this.client.get()));
      this.error.set(null);
      this.loaded.set(true);
    } catch (error) {
      this.error.set(describeError(error, 'Could not load your details.'));
    } finally {
      this.loading.set(false);
    }
  }

  async save(fullName: string, phone: string): Promise<boolean> {
    this.saving.set(true);
    this.error.set(null);
    this.saved.set(false);

    try {
      this.profileSignal.set(await firstValueFrom(this.client.update({ fullName, phone })));
      this.saved.set(true);
      return true;
    } catch (error) {
      this.error.set(describeError(error, 'Could not save your details.'));
      return false;
    } finally {
      this.saving.set(false);
    }
  }

  /**
   * Changing a password ends every other session, including this one's refresh token — so the
   * caller signs out afterwards rather than leaving somebody on a page whose next request will
   * fail for a reason they cannot see.
   */
  async changePassword(currentPassword: string, newPassword: string): Promise<boolean> {
    this.changingPassword.set(true);
    this.passwordError.set(null);
    this.passwordChanged.set(false);

    try {
      await firstValueFrom(this.client.changePassword({ currentPassword, newPassword }));
      this.passwordChanged.set(true);
      return true;
    } catch (error) {
      this.passwordError.set(
        describeError(error, 'Could not change your password. Check the current one.'),
      );
      return false;
    } finally {
      this.changingPassword.set(false);
    }
  }
}
