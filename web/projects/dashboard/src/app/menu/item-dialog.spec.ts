import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { CategoryResponse, MenuItemResponse } from 'api-client';
import { ItemDialog, ItemDialogData, ItemDialogResult } from './item-dialog';

/**
 * The dialog turns a filled-in form into the request the API expects. Most of that is plumbing;
 * the part worth testing is what it decides on the caller's behalf — where a moved item lands,
 * and what an empty description means.
 */
describe('ItemDialog', () => {
  let closed: ItemDialogResult | undefined;

  const categories = [
    { id: 'burgers', name: 'Burgers', sortOrder: 0, isActive: true },
    { id: 'fries', name: 'Fries', sortOrder: 1, isActive: true },
  ] as CategoryResponse[];

  const existing = {
    id: 'item-1',
    categoryId: 'burgers',
    name: 'Classic Smash',
    description: 'Two patties.',
    basePriceUsd: 7.5,
    isAvailable: true,
    sortOrder: 3,
  } as MenuItemResponse;

  function open(data: Partial<ItemDialogData>): ComponentFixture<ItemDialog> {
    closed = undefined;

    TestBed.configureTestingModule({
      imports: [ItemDialog],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        {
          provide: MAT_DIALOG_DATA,
          useValue: {
            categories,
            categoryId: 'burgers',
            // Each section's next free position, so a move can be told apart from a stay.
            nextSortOrder: (id: string) => (id === 'fries' ? 9 : 4),
            ...data,
          } satisfies ItemDialogData,
        },
        {
          provide: MatDialogRef,
          useValue: { close: (result: ItemDialogResult) => (closed = result) },
        },
      ],
    });

    const fixture = TestBed.createComponent(ItemDialog);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => TestBed.resetTestingModule());

  function submit(fixture: ComponentFixture<ItemDialog>): void {
    (fixture.componentInstance as unknown as { submit(): void }).submit();
  }

  function setForm(fixture: ComponentFixture<ItemDialog>, values: Record<string, unknown>): void {
    (
      fixture.componentInstance as unknown as { form: { patchValue(v: unknown): void } }
    ).form.patchValue(values);
  }

  it('sends a new item to the end of the section it was added from', () => {
    const fixture = open({});
    setForm(fixture, { name: 'Bacon Lab', price: 9.75 });

    submit(fixture);

    expect(closed?.request.sortOrder).toBe(4);
    expect(closed?.request.categoryId).toBe('burgers');
  });

  it('leaves an edited item where it was when the section did not change', () => {
    const fixture = open({ item: existing });
    setForm(fixture, { price: 8 });

    submit(fixture);

    // Recomputing the position on every edit would shuffle the menu each time a price changed.
    expect(closed?.request.sortOrder).toBe(3);
  });

  it('moves an item to the end of the section it was moved into', () => {
    const fixture = open({ item: existing });
    setForm(fixture, { categoryId: 'fries' });

    submit(fixture);

    // Keeping sortOrder 3 would drop it into the middle of Fries, among dishes it has nothing
    // to do with — and it would need the target section's count, not the one it left.
    expect(closed?.request.categoryId).toBe('fries');
    expect(closed?.request.sortOrder).toBe(9);
  });

  it('treats an emptied description as no description', () => {
    const fixture = open({ item: existing });
    setForm(fixture, { description: '   ' });

    submit(fixture);

    // Not the empty string: the column is nullable and a customer should see nothing there.
    expect(closed?.request.description).toBeUndefined();
  });

  it('trims a name before sending it', () => {
    const fixture = open({});
    setForm(fixture, { name: '  Spicy Inferno  ', price: 9 });

    submit(fixture);

    expect(closed?.request.name).toBe('Spicy Inferno');
  });

  it('refuses to submit a price the API would reject', () => {
    const fixture = open({});
    setForm(fixture, { name: 'Thing', price: '9.999' });

    submit(fixture);

    // decimal(10,2) on the server: a third place would be silently rounded, changing a price
    // the restaurant typed.
    expect(closed).toBeUndefined();
  });

  it('refuses to submit without a name', () => {
    const fixture = open({});
    setForm(fixture, { name: '', price: 5 });

    submit(fixture);

    expect(closed).toBeUndefined();
  });
});
