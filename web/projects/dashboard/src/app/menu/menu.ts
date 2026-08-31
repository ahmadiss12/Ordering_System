import { CurrencyPipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTooltipModule } from '@angular/material/tooltip';
import { CategoryResponse, MenuItemResponse } from 'api-client';
import { firstValueFrom } from 'rxjs';
import { ConfirmData, ConfirmDialog } from '../common/confirm-dialog';
import { ItemDialog, ItemDialogData, ItemDialogResult } from './item-dialog';
import { MenuStore } from './menu-store';
import { NamePromptData, NamePromptDialog } from './name-prompt-dialog';

/**
 * The menu editor: sections, and the items filed under them.
 *
 * Sections carry two states people confuse, so the screen keeps them apart. A hidden section is
 * still on the menu and still holds its items — customers just cannot see it, which is what a
 * restaurant wants for "Christmas specials" in March. Deleting is a different act, and this
 * screen does not offer it, because the API does not: a category with order history behind it
 * cannot simply disappear.
 */
@Component({
  selector: 'app-menu',
  providers: [MenuStore],
  imports: [
    CurrencyPipe,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatDialogModule,
    MatDividerModule,
    MatProgressBarModule,
    MatSlideToggleModule,
    MatTooltipModule,
  ],
  templateUrl: './menu.html',
  styleUrl: './menu.scss',
})
export class Menu {
  private readonly dialog = inject(MatDialog);

  protected readonly store = inject(MenuStore);

  constructor() {
    void this.store.load();
  }

  protected async addSection(): Promise<void> {
    const name = await this.askForName({
      title: 'New section',
      label: 'Section name',
      confirm: 'Add',
    });

    if (name) {
      await this.store.createCategory(name);
    }
  }

  protected async renameSection(category: CategoryResponse): Promise<void> {
    const name = await this.askForName({
      title: 'Rename section',
      label: 'Section name',
      value: category.name,
      confirm: 'Save',
    });

    if (name && name !== category.name) {
      await this.store.renameCategory(category, name);
    }
  }

  protected async toggleSection(category: CategoryResponse, isActive: boolean): Promise<void> {
    await this.store.setCategoryActive(category, isActive);
  }

  protected async move(category: CategoryResponse, direction: -1 | 1): Promise<void> {
    await this.store.moveCategory(category, direction);
  }

  // ------------------------------------------------------------------ items

  protected async addItem(category: CategoryResponse): Promise<void> {
    const result = await this.askAboutItem({
      categories: this.store.categories(),
      categoryId: category.id,
      nextSortOrder: (id) => this.store.nextItemSortOrder(id),
    });

    if (result) {
      await this.store.createItem(result.request, result.photo);
    }
  }

  protected async editItem(item: MenuItemResponse): Promise<void> {
    const result = await this.askAboutItem({
      categories: this.store.categories(),
      categoryId: item.categoryId,
      item,
      nextSortOrder: (id) => this.store.nextItemSortOrder(id),
    });

    if (!result) {
      return;
    }

    const saved = await this.store.updateItem(item, result.request, result.photo);

    // Only after the rest saved: dropping the photo of an item whose edit was refused would
    // lose something the person never asked to lose.
    if (saved && result.removePhoto) {
      await this.store.removeItemPhoto(item);
    }
  }

  protected async toggleItem(item: MenuItemResponse, isAvailable: boolean): Promise<void> {
    await this.store.setItemAvailable(item, isAvailable);
  }

  protected async moveItem(item: MenuItemResponse, direction: -1 | 1): Promise<void> {
    await this.store.moveItem(item, direction);
  }

  protected async removeItem(item: MenuItemResponse): Promise<void> {
    const confirmed = await this.confirm({
      title: `Remove ${item.name}?`,
      message:
        'It comes off the menu straight away. Past orders that included it are not affected.',
      confirm: 'Remove',
      destructive: true,
    });

    if (confirmed) {
      await this.store.deleteItem(item);
    }
  }

  // ------------------------------------------------------------------ helpers

  protected isFirst(index: number): boolean {
    return index === 0;
  }

  protected isLast(index: number): boolean {
    return index === this.store.sections().length - 1;
  }

  private async askAboutItem(data: ItemDialogData): Promise<ItemDialogResult | undefined> {
    return firstValueFrom(
      this.dialog
        .open<ItemDialog, ItemDialogData, ItemDialogResult>(ItemDialog, { data })
        .afterClosed(),
    );
  }

  private async confirm(data: ConfirmData): Promise<boolean | undefined> {
    return firstValueFrom(
      this.dialog.open<ConfirmDialog, ConfirmData, boolean>(ConfirmDialog, { data }).afterClosed(),
    );
  }

  private async askForName(data: NamePromptData): Promise<string | undefined> {
    return firstValueFrom(
      this.dialog
        .open<NamePromptDialog, NamePromptData, string>(NamePromptDialog, { data })
        .afterClosed(),
    );
  }
}
