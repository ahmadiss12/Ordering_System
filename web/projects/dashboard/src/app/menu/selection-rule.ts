/**
 * How many options a customer must and may pick from one group.
 *
 * The API stores this as two integers, `MinSelect` and a nullable `MaxSelect`. Those two numbers
 * are precise and almost nobody reads them correctly: (1, 1) and (0, 1) look nearly identical and
 * mean "you must choose a size" versus "you may add a sauce". A restaurant owner should never be
 * asked to encode their intent as a pair of integers, so the editor works in named rules and
 * shows the sentence a customer would read.
 */
export interface SelectionRule {
  readonly minSelect: number;
  /** No maximum. Stored as null; the generated client widens that to undefined. */
  readonly maxSelect: number | null;
}

export type RulePresetId = 'anyNumber' | 'atMostOne' | 'exactlyOne' | 'atLeastOne' | 'custom';

export interface RulePreset {
  readonly id: RulePresetId;
  /** How the owner picks it. */
  readonly label: string;
  /** Why they would. */
  readonly hint: string;
  readonly rule?: SelectionRule;
}

/**
 * The four rules that cover almost every real menu, plus a way out.
 *
 * Ordered by how often a menu needs them, not by what the numbers do: extras and toppings are
 * the common case, and a required single choice is the other one. Anything else — pick exactly
 * two sides, choose three to five — goes through Custom, where the numbers do appear, next to a
 * sentence saying what they will mean.
 */
export const RULE_PRESETS: readonly RulePreset[] = [
  {
    id: 'anyNumber',
    label: 'Optional — any number',
    hint: 'Extras and toppings. The customer can take none, or all of them.',
    rule: { minSelect: 0, maxSelect: null },
  },
  {
    id: 'atMostOne',
    label: 'Optional — one at most',
    hint: 'A single add-on, like one dip.',
    rule: { minSelect: 0, maxSelect: 1 },
  },
  {
    id: 'exactlyOne',
    label: 'Required — exactly one',
    hint: 'A choice the dish cannot be made without, like size.',
    rule: { minSelect: 1, maxSelect: 1 },
  },
  {
    id: 'atLeastOne',
    label: 'Required — one or more',
    hint: 'They must pick something, and may pick several.',
    rule: { minSelect: 1, maxSelect: null },
  },
  {
    id: 'custom',
    label: 'Something else',
    hint: 'Set the smallest and largest number yourself.',
  },
];

/** True when the customer cannot get past this group without choosing. */
export function isRequired(rule: SelectionRule): boolean {
  return rule.minSelect >= 1;
}

/**
 * The instruction a customer reads above the group. This is the sentence the whole screen exists
 * to make visible, so an owner never has to work out what their numbers will do.
 */
export function describeRule(rule: SelectionRule): string {
  const { minSelect } = rule;
  const max = rule.maxSelect;

  if (minSelect === 0) {
    if (max === null) {
      return 'Choose as many as you like';
    }
    return max === 1 ? 'Choose 1 — optional' : `Choose up to ${max}`;
  }

  if (max === null) {
    return minSelect === 1 ? 'Choose at least 1' : `Choose at least ${minSelect}`;
  }

  if (minSelect === max) {
    return minSelect === 1 ? 'Choose 1' : `Choose ${minSelect}`;
  }

  return `Choose ${minSelect} to ${max}`;
}

/** A short label for a list, where the full sentence would be too long. */
export function summariseRule(rule: SelectionRule): string {
  return `${isRequired(rule) ? 'Required' : 'Optional'} · ${describeRule(rule)}`;
}

/** Which preset a stored rule corresponds to, or 'custom' when none of them fits. */
export function presetFor(rule: SelectionRule): RulePresetId {
  const match = RULE_PRESETS.find(
    (preset) =>
      preset.rule?.minSelect === rule.minSelect && preset.rule.maxSelect === rule.maxSelect,
  );
  return match?.id ?? 'custom';
}

/**
 * Normalises what the API sends into a rule.
 *
 * `maxSelect` arrives as null over the wire but is typed `number | undefined` by the generated
 * client, so both have to collapse to the same "no maximum". Written with `??` rather than a
 * falsy check on purpose: `||` would turn a maximum of zero into no maximum at all — a group
 * nobody can pick from into one where anything goes.
 */
export function ruleFrom(source: { minSelect?: number; maxSelect?: number | null }): SelectionRule {
  return {
    minSelect: source.minSelect ?? 0,
    maxSelect: source.maxSelect ?? null,
  };
}

/** Whether the rule is one the API and the database will accept. */
export function ruleIsValid(rule: SelectionRule): boolean {
  if (!Number.isInteger(rule.minSelect) || rule.minSelect < 0) {
    return false;
  }

  if (rule.maxSelect === null) {
    return true;
  }

  // Mirrors CK_OptionGroups_SelectRange. Catching it here means a sentence rather than a
  // constraint violation coming back from SQL Server.
  return (
    Number.isInteger(rule.maxSelect) && rule.maxSelect >= 1 && rule.minSelect <= rule.maxSelect
  );
}
