import { Component, computed, input, model, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import {
  RULE_PRESETS,
  RulePresetId,
  SelectionRule,
  describeRule,
  presetFor,
  ruleIsValid,
} from './selection-rule';

/**
 * Picks how many options a customer must and may choose.
 *
 * The design point of the whole step: the owner picks a named rule, and underneath sees the exact
 * words a customer will read. Nobody is asked what a minimum of 1 and a maximum of 1 does — they
 * are shown "Choose 1" and can tell at a glance whether that is what they meant. The numbers only
 * appear under "Something else", and even there the sentence is right beside them.
 */
@Component({
  selector: 'app-rule-picker',
  imports: [FormsModule, MatRadioModule, MatFormFieldModule, MatInputModule, MatIconModule],
  template: `
    <fieldset class="picker">
      <legend>{{ legend() }}</legend>

      <mat-radio-group [ngModel]="preset()" (ngModelChange)="choosePreset($event)">
        @for (option of presets; track option.id) {
          <mat-radio-button [value]="option.id">
            <span class="label">{{ option.label }}</span>
            <span class="hint">{{ option.hint }}</span>
          </mat-radio-button>
        }
      </mat-radio-group>

      @if (preset() === 'custom') {
        <div class="numbers">
          <mat-form-field appearance="outline">
            <mat-label>At least</mat-label>
            <input
              matInput
              type="number"
              min="0"
              [ngModel]="rule().minSelect"
              (ngModelChange)="setMin($event)"
            />
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>At most</mat-label>
            <input
              matInput
              type="number"
              min="1"
              [ngModel]="rule().maxSelect"
              (ngModelChange)="setMax($event)"
            />
            <mat-hint>Leave empty for no limit</mat-hint>
          </mat-form-field>
        </div>
      }

      <p class="preview" [class.invalid]="!valid()">
        <mat-icon>{{ valid() ? 'visibility' : 'error_outline' }}</mat-icon>
        @if (valid()) {
          <span
            >Customers will see <strong>{{ sentence() }}</strong></span
          >
        } @else {
          <span>The smallest number cannot be larger than the largest.</span>
        }
      </p>
    </fieldset>
  `,
  styleUrl: './rule-picker.scss',
})
export class RulePicker {
  readonly legend = input('How many can a customer choose?');
  readonly rule = model.required<SelectionRule>();

  protected readonly presets = RULE_PRESETS;

  /**
   * Held rather than derived, so that choosing "Something else" for a rule that happens to match
   * a preset does not immediately snap the radio back to that preset as the numbers are typed.
   */
  private readonly explicitPreset = signal<RulePresetId | null>(null);

  protected readonly preset = computed(() => this.explicitPreset() ?? presetFor(this.rule()));
  protected readonly valid = computed(() => ruleIsValid(this.rule()));
  protected readonly sentence = computed(() => describeRule(this.rule()));

  protected choosePreset(id: RulePresetId): void {
    this.explicitPreset.set(id);

    const chosen = RULE_PRESETS.find((preset) => preset.id === id);
    if (chosen?.rule) {
      this.rule.set(chosen.rule);
    }
  }

  protected setMin(value: unknown): void {
    this.rule.update((current) => ({ ...current, minSelect: toCount(value) ?? 0 }));
  }

  protected setMax(value: unknown): void {
    // An empty box is "no limit", which is a different thing from zero — and zero would be a
    // group nobody can pick anything from.
    this.rule.update((current) => ({ ...current, maxSelect: toCount(value) }));
  }
}

function toCount(value: unknown): number | null {
  if (value === '' || value === null || value === undefined) {
    return null;
  }

  const parsed = Number(value);
  return Number.isFinite(parsed) ? Math.trunc(parsed) : null;
}
