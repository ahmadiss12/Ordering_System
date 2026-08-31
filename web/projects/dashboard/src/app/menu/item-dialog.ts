import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { CategoryResponse, CreateMenuItemRequest, MenuItemResponse } from 'api-client';

export interface ItemDialogData {
  readonly categories: readonly CategoryResponse[];
  /** The section the "add" button was pressed in, or the item's own when editing. */
  readonly categoryId: string;
  readonly item?: MenuItemResponse;
  /**
   * Where a new item lands in a given section. A function rather than a number because the
   * section can be changed inside the dialog, and the answer differs per section.
   */
  readonly nextSortOrder: (categoryId: string) => number;
}

export interface ItemDialogResult {
  readonly request: CreateMenuItemRequest;
  /** A newly chosen photo, uploaded separately once the item has an id. */
  readonly photo?: File;
  readonly removePhoto: boolean;
}

/** Anything larger is refused by the API, so it is refused here with a sentence instead. */
const MAX_PHOTO_BYTES = 8 * 1024 * 1024;

/**
 * One dish: what it is called, what it costs, which section it sits in, and its photo.
 *
 * Availability is deliberately absent. The API changes it through its own endpoint because it is
 * pressed mid-service when the kitchen runs out — putting it in a form that has to be filled in
 * and saved would make the fastest action on the screen the slowest.
 */
@Component({
  selector: 'app-item-dialog',
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatIconModule,
  ],
  templateUrl: './item-dialog.html',
  styleUrl: './item-dialog.scss',
})
export class ItemDialog {
  protected readonly data = inject<ItemDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject(MatDialogRef<ItemDialog, ItemDialogResult>);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly isEdit = !!this.data.item;
  protected readonly photoError = signal<string | null>(null);

  private readonly chosenPhoto = signal<File | null>(null);
  private readonly chosenPhotoUrl = signal<string | null>(null);
  private readonly photoRemoved = signal(false);

  /** What the preview shows: a newly chosen file, the stored photo, or nothing. */
  protected readonly previewUrl = computed(
    () =>
      this.chosenPhotoUrl() ?? (this.photoRemoved() ? null : (this.data.item?.imageUrl ?? null)),
  );

  protected readonly form = this.formBuilder.nonNullable.group({
    name: [this.data.item?.name ?? '', [Validators.required, Validators.maxLength(200)]],
    description: [this.data.item?.description ?? '', [Validators.maxLength(1000)]],
    // Mirrors the API's money rules, so a price that would be rejected is caught before the trip.
    price: [
      this.data.item?.basePriceUsd ?? 0,
      [Validators.required, Validators.min(0), Validators.pattern(/^\d+(\.\d{1,2})?$/)],
    ],
    categoryId: [this.data.item?.categoryId ?? this.data.categoryId, [Validators.required]],
  });

  protected choosePhoto(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    this.photoError.set(null);

    if (!file) {
      return;
    }

    if (!file.type.startsWith('image/')) {
      this.photoError.set('That file is not an image.');
      return;
    }

    if (file.size > MAX_PHOTO_BYTES) {
      // Saying so here saves an 8MB upload that the server would only refuse on arrival.
      this.photoError.set('That photo is larger than 8 MB. Choose a smaller one.');
      return;
    }

    this.revokePreview();
    this.chosenPhoto.set(file);
    this.chosenPhotoUrl.set(URL.createObjectURL(file));
    this.photoRemoved.set(false);
  }

  protected removePhoto(): void {
    this.revokePreview();
    this.chosenPhoto.set(null);
    this.chosenPhotoUrl.set(null);
    this.photoRemoved.set(true);
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { name, description, price, categoryId } = this.form.getRawValue();
    const trimmedDescription = description.trim();

    this.dialogRef.close({
      request: {
        categoryId,
        name: name.trim(),
        // An empty box means no description, not a description that is the empty string.
        description: trimmedDescription.length > 0 ? trimmedDescription : undefined,
        basePriceUsd: Number(price),
        // Keeping its place only makes sense while it stays put. Moved to another section it
        // goes to the end of that one, where it is visible — rather than inheriting a position
        // from the section it left and landing in the middle of dishes it has nothing to do with.
        sortOrder:
          this.data.item && categoryId === this.data.item.categoryId
            ? this.data.item.sortOrder
            : this.data.nextSortOrder(categoryId),
      },
      photo: this.chosenPhoto() ?? undefined,
      removePhoto: this.photoRemoved() && !this.chosenPhoto(),
    });
  }

  /** Object URLs hold the file in memory until released, and this dialog opens repeatedly. */
  private revokePreview(): void {
    const url = this.chosenPhotoUrl();
    if (url) {
      URL.revokeObjectURL(url);
    }
  }
}
