import { CurrencyPipe } from '@angular/common';
import { Component, Injector, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatTabsModule } from '@angular/material/tabs';
import {
  CategoryResponse,
  MenuItemResponse,
  OptionGroupResponse,
  OptionResponse,
} from 'api-client';
import { firstValueFrom } from 'rxjs';
import { ConfirmData, ConfirmDialog } from '../common/confirm-dialog';
import { ItemDialog, ItemDialogData, ItemDialogResult } from './item-dialog';
import { ItemOptionsDialog, ItemOptionsDialogData } from './item-options-dialog';
import { OptionDialog, OptionDialogData, OptionDialogResult } from './option-dialog';
import {
  OptionGroupDialog,
  OptionGroupDialogData,
  OptionGroupDialogResult,
} from './option-group-dialog';
import { ruleFrom, summariseRule } from './selection-rule';
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
    MatTabsModule,
    MatTooltipModule,
  ],
  templateUrl: './menu.html',
  styleUrl: './menu.scss',
})
export class Menu {
  private readonly dialog = inject(MatDialog);

  /**
   * MatDialog builds its content in the root injector, so a dialog cannot see MenuStore — which
   * this component provides, deliberately, so the menu is dropped on leaving the page. Handing
   * the dialog this injector is what lets it share the same store rather than reaching for a
   * root-provided one that does not exist.
   */
  private readonly injector = inject(Injector);

  protected readonly store = inject(MenuStore);

  /** Which tab is showing. The header's primary button follows it. */
  protected readonly tab = signal(0);

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

  // ------------------------------------------------------------------ option groups

  protected summarise(source: { minSelect?: number; maxSelect?: number | null }): string {
    return summariseRule(ruleFrom(source));
  }

  protected async addOptionGroup(): Promise<void> {
    const result = await this.askAboutGroup({});
    if (result) {
      await this.store.createOptionGroup(result.name, result.rule);
    }
  }

  protected async editOptionGroup(group: OptionGroupResponse): Promise<void> {
    const result = await this.askAboutGroup({ group });
    if (result) {
      await this.store.updateOptionGroup(group, { name: result.name, rule: result.rule });
    }
  }

  protected async addOption(group: OptionGroupResponse): Promise<void> {
    const result = await this.askAboutOption({
      groupName: group.name,
      sortOrder: group.options.length,
    });

    if (result) {
      await this.store.addOption(group, result);
    }
  }

  protected async editOption(group: OptionGroupResponse, option: OptionResponse): Promise<void> {
    const result = await this.askAboutOption({
      groupName: group.name,
      option,
      sortOrder: option.sortOrder,
    });

    if (result) {
      await this.store.updateOption(group, option.id, result);
    }
  }

  protected async toggleOption(
    group: OptionGroupResponse,
    option: OptionResponse,
    isAvailable: boolean,
  ): Promise<void> {
    await this.store.updateOption(group, option.id, {
      name: option.name,
      priceDeltaUsd: option.priceDeltaUsd,
      maxQuantity: option.maxQuantity,
      sortOrder: option.sortOrder,
      isAvailable,
    });
  }

  protected async editItemOptions(item: MenuItemResponse): Promise<void> {
    // Its own dialog rather than a section of the item form: what a dish costs is one question,
    // and which choices it offers is another.
    await firstValueFrom(
      this.dialog
        .open<ItemOptionsDialog, ItemOptionsDialogData, void>(ItemOptionsDialog, {
          data: { item },
          injector: this.injector,
        })
        .afterClosed(),
    );
  }

  // ------------------------------------------------------------------ helpers

  protected isFirst(index: number): boolean {
    return index === 0;
  }

  protected isLast(index: number): boolean {
    return index === this.store.sections().length - 1;
  }

  private async askAboutGroup(
    data: OptionGroupDialogData,
  ): Promise<OptionGroupDialogResult | undefined> {
    return firstValueFrom(
      this.dialog
        .open<OptionGroupDialog, OptionGroupDialogData, OptionGroupDialogResult>(
          OptionGroupDialog,
          { data },
        )
        .afterClosed(),
    );
  }

  private async askAboutOption(data: OptionDialogData): Promise<OptionDialogResult | undefined> {
    return firstValueFrom(
      this.dialog
        .open<OptionDialog, OptionDialogData, OptionDialogResult>(OptionDialog, { data })
        .afterClosed(),
    );
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
