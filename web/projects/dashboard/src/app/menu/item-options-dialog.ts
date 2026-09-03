import { Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AttachedOptionGroupResponse, MenuItemResponse, OptionGroupResponse } from 'api-client';
import { MenuStore } from './menu-store';
import { RulePicker } from './rule-picker';
import { SelectionRule, describeRule, ruleFrom, summariseRule } from './selection-rule';

export interface ItemOptionsDialogData {
  readonly item: MenuItemResponse;
}

/** One row: a group the restaurant has, and how it applies to this dish. */
interface GroupRow {
  readonly group: OptionGroupResponse;
  readonly attached: boolean;
  /** The rule in force for this item — the group's own, unless overridden. */
  readonly rule: SelectionRule;
  readonly groupRule: SelectionRule;
  readonly overridden: boolean;
  readonly sortOrder: number;
}

/**
 * Which groups of choices a dish offers, and whether it follows the group's rule or its own.
 *
 * The override is the reason groups are shared at all: one "Sauces" group serves every burger,
 * while the mixed platter may require two of them. The screen names that difference outright —
 * "Same as the group" or "Changed for this dish" — because a single resolved number gives no way
 * to tell a deliberate override from the group's own default, and saving one would quietly turn
 * an inherited value into an override nobody chose.
 */
@Component({
  selector: 'app-item-options-dialog',
  imports: [
    CurrencyPipe,
    MatDialogModule,
    MatButtonModule,
    MatCheckboxModule,
    MatDividerModule,
    MatIconModule,
    MatProgressBarModule,
    MatTooltipModule,
    RulePicker,
  ],
  templateUrl: './item-options-dialog.html',
  styleUrl: './item-options-dialog.scss',
})
export class ItemOptionsDialog {
  protected readonly data = inject<ItemOptionsDialogData>(MAT_DIALOG_DATA);
  protected readonly store = inject(MenuStore);

  private readonly attached = signal<readonly AttachedOptionGroupResponse[]>([]);
  protected readonly loading = signal(true);

  /** Which row has its override editor open. Only one at a time; the dialog is not that tall. */
  protected readonly editingOverride = signal<string | null>(null);
  protected readonly draftRule = signal<SelectionRule>({ minSelect: 0, maxSelect: null });

  protected readonly rows = computed<readonly GroupRow[]>(() => {
    const links = this.attached();

    return this.store.optionGroups().map((group) => {
      const link = links.find((l) => l.optionGroupId === group.id);
      const groupRule = ruleFrom(group);

      return {
        group,
        attached: !!link,
        groupRule,
        rule: link
          ? ruleFrom({ minSelect: link.effectiveMinSelect, maxSelect: link.effectiveMaxSelect })
          : groupRule,
        // Either bound overridden makes this item's rule its own. The unresolved values are
        // exactly what the 8a endpoint was added to return.
        overridden: !!link && (isSet(link.minSelectOverride) || isSet(link.maxSelectOverride)),
        sortOrder: link?.sortOrder ?? 0,
      };
    });
  });

  protected readonly attachedCount = computed(() => this.rows().filter((r) => r.attached).length);

  /**
   * What the dish will actually show if the draft is saved, resolved exactly as the server
   * resolves it: `override ?? group value`, per bound.
   *
   * This exists because null carries two meanings that collide. On the wire it means "inherit
   * this bound from the group", so there is no way to say "this dish has no maximum" once the
   * group has one — a dish can lower the group's limit but never remove it. Rather than let the
   * picker offer "any number" and quietly store something else, the dialog shows the resolved
   * sentence and says why it differs.
   */
  protected readonly overrideResult = computed<SelectionRule | null>(() => {
    const row = this.rows().find((r) => r.group.id === this.editingOverride());
    if (!row) {
      return null;
    }

    const draft = this.draftRule();
    return {
      minSelect: draft.minSelect,
      maxSelect: draft.maxSelect === null ? row.groupRule.maxSelect : draft.maxSelect,
    };
  });

  /** True when the group's own maximum will survive an override that asked for none. */
  protected readonly limitCannotBeRemoved = computed(() => {
    const result = this.overrideResult();
    return !!result && this.draftRule().maxSelect === null && result.maxSelect !== null;
  });

  constructor() {
    void this.reload();
  }

  protected describe(rule: SelectionRule): string {
    return describeRule(rule);
  }

  protected summarise(rule: SelectionRule): string {
    return summariseRule(rule);
  }

  protected async toggleAttached(row: GroupRow, attached: boolean): Promise<void> {
    const ok = attached
      ? await this.store.attachOptionGroup(this.data.item.id, {
          optionGroupId: row.group.id,
          // Appended rather than inserted, so attaching one group does not reorder the others.
          sortOrder: this.attachedCount(),
          minSelectOverride: null,
          maxSelectOverride: null,
        })
      : await this.store.detachOptionGroup(this.data.item.id, row.group.id);

    if (ok) {
      this.editingOverride.set(null);
      await this.reload();
    }
  }

  protected startOverride(row: GroupRow): void {
    this.draftRule.set(row.rule);
    this.editingOverride.set(row.group.id);
  }

  protected cancelOverride(): void {
    this.editingOverride.set(null);
  }

  protected async saveOverride(row: GroupRow): Promise<void> {
    const rule = this.draftRule();

    const ok = await this.store.attachOptionGroup(this.data.item.id, {
      optionGroupId: row.group.id,
      sortOrder: row.sortOrder,
      minSelectOverride: rule.minSelect,
      maxSelectOverride: rule.maxSelect ?? null,
    });

    if (ok) {
      this.editingOverride.set(null);
      await this.reload();
    }
  }

  /** Puts the dish back on the group's own rule, rather than a copy of it. */
  protected async clearOverride(row: GroupRow): Promise<void> {
    const ok = await this.store.attachOptionGroup(this.data.item.id, {
      optionGroupId: row.group.id,
      sortOrder: row.sortOrder,
      minSelectOverride: null,
      maxSelectOverride: null,
    });

    if (ok) {
      await this.reload();
    }
  }

  private async reload(): Promise<void> {
    this.loading.set(true);
    const links = await this.store.loadItemOptionGroups(this.data.item.id);
    if (links) {
      this.attached.set(links);
    }
    this.loading.set(false);
  }
}

/** Null and undefined both mean "not overridden"; zero is a real minimum. */
function isSet(value: number | null | undefined): boolean {
  return value !== undefined && value !== null;
}
