import { TestBed } from '@angular/core/testing';
import {
  InviteStaffRequest,
  InvitedStaffResponse,
  RestaurantStaffClient,
  StaffMemberResponse,
  StaffRoleType,
} from 'api-client';
import { Observable, of, throwError } from 'rxjs';
import { StaffStore } from './staff-store';

/**
 * The staff list.
 *
 * <p>
 * What is worth testing here is not that a list renders. It is that the screen never offers a
 * button which would leave the restaurant without an owner, and that it never claims to have
 * emailed somebody who was already here.
 * </p>
 */
describe('StaffStore', () => {
  let client: FakeStaffClient;

  beforeEach(() => {
    client = new FakeStaffClient();
    TestBed.configureTestingModule({
      providers: [StaffStore, { provide: RestaurantStaffClient, useValue: client }],
    });
  });

  it('will not offer to demote or remove the only owner', async () => {
    client.returns([member('a', 'Layla', StaffRoleType.Owner), member('b', 'Sami')]);
    const store = await loaded();

    expect(store.isLastOwner(store.members()[0])).toBe(true);

    // Staff are never the last owner, however few of them there are - there is nothing to protect.
    expect(store.isLastOwner(store.members()[1])).toBe(false);
  });

  it('stops protecting an owner once there is a second one', async () => {
    client.returns([
      member('a', 'Layla', StaffRoleType.Owner),
      member('b', 'Sami', StaffRoleType.Owner),
    ]);
    const store = await loaded();

    expect(store.isLastOwner(store.members()[0])).toBe(false);
    expect(store.isLastOwner(store.members()[1])).toBe(false);
  });

  it('reloads the whole list after a change, not just the row that changed', async () => {
    client.returns([member('a', 'Layla', StaffRoleType.Owner), member('b', 'Sami')]);
    const store = await loaded();

    // Promoting Sami is what stops Layla being the last owner. A store that patched only the
    // changed row would leave Layla's buttons hidden with no reason left to hide them.
    client.returns([
      member('a', 'Layla', StaffRoleType.Owner),
      member('b', 'Sami', StaffRoleType.Owner),
    ]);
    await store.setRole(store.members()[1], StaffRoleType.Owner);

    expect(store.isLastOwner(store.members()[0])).toBe(false);
  });

  it('says an invitation was emailed when one was', async () => {
    client.returns([]);
    const store = await loaded();

    client.invites(member('c', 'Newcomer', StaffRoleType.Staff, true), true);
    await store.invite(request('new@example.test'));

    expect(store.invited()?.invitationEmailed).toBe(true);
  });

  it('does not claim to have emailed a link the mail server refused', async () => {
    client.returns([]);
    const store = await loaded();

    // Added, but they cannot sign in and nothing went out. The server reports this as a success
    // on purpose - the row is committed - so the screen is the only place it can be said.
    client.invites(member('c', 'Newcomer', StaffRoleType.Staff, true), false);
    await store.invite(request('new@example.test'));

    expect(store.invited()?.invitationEmailed).toBe(false);
    expect(store.invited()?.member.mustSetPassword).toBe(true);
  });

  it('does not claim to have emailed somebody who already had an account', async () => {
    client.returns([]);
    const store = await loaded();

    // No link is sent to an existing customer - they sign in with the password they already
    // have. A screen that said "invitation emailed" would be telling an owner to expect
    // something that is never going to arrive.
    client.invites(member('c', 'Old Regular', StaffRoleType.Staff, false), false);
    await store.invite(request('regular@example.test'));

    expect(store.invited()?.invitationEmailed).toBe(false);
    expect(store.invited()?.member.mustSetPassword).toBe(false);
  });

  it('keeps the invitation message through the reload that follows it', async () => {
    client.returns([]);
    const store = await loaded();

    client.invites(member('c', 'Newcomer', StaffRoleType.Staff, true), true);
    await store.invite(request('new@example.test'));

    // The reload runs after the invite and would wipe a message set before it. This is the same
    // mistake the order queue made with its refusal notice, which is why it is pinned here.
    expect(store.invited()).not.toBeNull();
  });

  it('clears the invitation message when something else happens', async () => {
    client.returns([member('a', 'Layla', StaffRoleType.Owner), member('b', 'Sami')]);
    const store = await loaded();

    client.invites(member('c', 'Newcomer', StaffRoleType.Staff, true), true);
    await store.invite(request('new@example.test'));

    await store.setRole(store.members()[1], StaffRoleType.Owner);

    // Otherwise "invitation emailed to..." sits under a list somebody has since edited twice.
    expect(store.invited()).toBeNull();
  });

  it('does not pretend to have a list it could not load', async () => {
    client.fails();
    const store = TestBed.inject(StaffStore);
    await store.load();

    // An empty list drawn confidently would tell an owner they work here alone.
    expect(store.loaded()).toBe(false);
    expect(store.error()).not.toBeNull();
  });

  it('names the person a failed change was about', async () => {
    client.returns([member('a', 'Layla', StaffRoleType.Owner), member('b', 'Sami')]);
    const store = await loaded();

    client.fails();
    await store.remove(store.members()[1]);

    expect(store.error()).toContain('Sami');
  });

  async function loaded(): Promise<StaffStore> {
    const store = TestBed.inject(StaffStore);
    await store.load();
    return store;
  }
});

function member(
  userId: string,
  fullName: string,
  staffRole: StaffRoleType = StaffRoleType.Staff,
  mustSetPassword = false,
): StaffMemberResponse {
  return {
    userId,
    email: `${fullName.toLowerCase()}@example.test`,
    fullName,
    staffRole,
    mustSetPassword,
    isYou: false,
    createdAt: new Date(),
  } as StaffMemberResponse;
}

function request(email: string): InviteStaffRequest {
  return { email, fullName: 'Someone', phone: null, staffRole: StaffRoleType.Staff };
}

class FakeStaffClient {
  private members: StaffMemberResponse[] = [];
  private invited: InvitedStaffResponse | null = null;
  private failing = false;

  returns(members: StaffMemberResponse[]): void {
    this.members = members;
  }

  invites(member: StaffMemberResponse, invitationEmailed: boolean): void {
    this.invited = { member, invitationEmailed } as InvitedStaffResponse;
  }

  fails(): void {
    this.failing = true;
  }

  list(): Observable<StaffMemberResponse[]> {
    return this.failing ? this.broken() : of(this.members);
  }

  invite(): Observable<InvitedStaffResponse> {
    return this.failing ? this.broken() : of(this.invited!);
  }

  setRole(userId: string, body: { staffRole: StaffRoleType }): Observable<StaffMemberResponse> {
    if (this.failing) {
      return this.broken();
    }

    const found = this.members.find((m) => m.userId === userId)!;
    return of({ ...found, staffRole: body.staffRole });
  }

  remove(): Observable<void> {
    return this.failing ? this.broken() : of(undefined);
  }

  private broken<T>(): Observable<T> {
    return throwError(() => new Error('the API is not answering'));
  }
}
