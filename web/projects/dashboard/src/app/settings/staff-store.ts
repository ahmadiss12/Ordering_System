import { Injectable, computed, inject, signal } from '@angular/core';
import {
  InviteStaffRequest,
  InvitedStaffResponse,
  RestaurantStaffClient,
  StaffMemberResponse,
  StaffRoleType,
  describeError,
} from 'api-client';
import { firstValueFrom } from 'rxjs';

/**
 * Who works here.
 *
 * <h4>Reloaded after every change, rather than patched</h4>
 *
 * A change to one person can change what is true of another: promoting a colleague is what makes
 * the last owner no longer the last, and demoting somebody may be the thing that takes the screen
 * away from the person doing it. Splicing the one changed row back into the list would leave the
 * rest stale, and the buttons drawn from it wrong — so the list is asked for again.
 */
@Injectable()
export class StaffStore {
  private readonly client = inject(RestaurantStaffClient);

  private readonly membersSignal = signal<StaffMemberResponse[]>([]);

  readonly members = this.membersSignal.asReadonly();
  readonly loading = signal(true);
  readonly loaded = signal(false);
  readonly error = signal<string | null>(null);

  /** Whose row is mid-request, so only that row's controls are disabled. */
  readonly busy = signal<string | null>(null);
  readonly inviting = signal(false);

  /**
   * What the last invitation actually did. Three outcomes, not one: a link was emailed, there was
   * no link to send because they already had an account, or there was one and the mail failed.
   * The screen has to be able to say which — an owner told "invitation emailed" who then waits
   * for a link that never went out has no way to find that out.
   */
  readonly invited = signal<InvitedStaffResponse | null>(null);

  /**
   * Whether the last owner is about to be left alone. The buttons that would break the restaurant
   * are hidden rather than left to be pressed and refused — the server refuses either way, this
   * is only about not offering it.
   */
  readonly ownerCount = computed(
    () => this.membersSignal().filter((m) => m.staffRole === StaffRoleType.Owner).length,
  );

  isLastOwner(member: StaffMemberResponse): boolean {
    return member.staffRole === StaffRoleType.Owner && this.ownerCount() <= 1;
  }

  async load(): Promise<void> {
    this.loading.set(true);

    // Asking the server for the truth clears whatever the screen was still saying about the last
    // thing somebody did. Otherwise "invitation emailed to..." can end up sitting above a list
    // that has since failed to load, or above one it no longer describes.
    this.invited.set(null);

    try {
      this.membersSignal.set(await firstValueFrom(this.client.list()));
      this.error.set(null);
      this.loaded.set(true);
    } catch (error) {
      this.error.set(describeError(error, 'Could not load your staff list.'));
    } finally {
      this.loading.set(false);
    }
  }

  async invite(request: InviteStaffRequest): Promise<boolean> {
    this.inviting.set(true);
    this.error.set(null);
    this.invited.set(null);

    try {
      const invited = await firstValueFrom(this.client.invite(request));

      await this.load();

      // Set after the reload, because load() clears it. The message is the only place the screen
      // says whether an email actually went out, and losing it would leave an owner waiting for
      // a link that was never sent.
      this.invited.set(invited);
      return true;
    } catch (error) {
      this.error.set(describeError(error, `Could not invite ${request.email}.`));
      return false;
    } finally {
      this.inviting.set(false);
    }
  }

  async setRole(member: StaffMemberResponse, staffRole: StaffRoleType): Promise<boolean> {
    return this.change(
      member,
      () => firstValueFrom(this.client.setRole(member.userId, { staffRole })),
      `Could not change ${member.fullName}'s role.`,
    );
  }

  async remove(member: StaffMemberResponse): Promise<boolean> {
    return this.change(
      member,
      () => firstValueFrom(this.client.remove(member.userId)),
      `Could not remove ${member.fullName}.`,
    );
  }

  private async change(
    member: StaffMemberResponse,
    request: () => Promise<unknown>,
    whenItFails: string,
  ): Promise<boolean> {
    this.busy.set(member.userId);
    this.error.set(null);
    this.invited.set(null);

    try {
      await request();
      await this.load();
      return true;
    } catch (error) {
      this.error.set(describeError(error, whenItFails));
      return false;
    } finally {
      this.busy.set(null);
    }
  }
}
