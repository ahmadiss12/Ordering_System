import {
  RULE_PRESETS,
  describeRule,
  isRequired,
  presetFor,
  ruleFrom,
  ruleIsValid,
  summariseRule,
} from './selection-rule';

/**
 * The translation between two integers and a sentence a person can act on.
 *
 * Worth testing thoroughly because it is the only thing standing between a restaurant owner and
 * a pair of numbers whose difference — (1,1) versus (0,1) — is the difference between a size
 * they must pick and a sauce they may add.
 */
describe('selection rules', () => {
  describe('describeRule', () => {
    it.each([
      [0, null, 'Choose as many as you like'],
      [0, 1, 'Choose 1 — optional'],
      [0, 3, 'Choose up to 3'],
      [1, 1, 'Choose 1'],
      [1, null, 'Choose at least 1'],
      [2, null, 'Choose at least 2'],
      [2, 2, 'Choose 2'],
      [2, 4, 'Choose 2 to 4'],
    ])('reads (%s, %s) as "%s"', (minSelect, maxSelect, expected) => {
      expect(describeRule({ minSelect, maxSelect })).toBe(expected);
    });

    it('keeps the two one-option rules distinguishable', () => {
      // The pair this whole module exists for: same maximum, opposite meaning.
      expect(describeRule({ minSelect: 1, maxSelect: 1 })).not.toBe(
        describeRule({ minSelect: 0, maxSelect: 1 }),
      );
    });
  });

  describe('isRequired', () => {
    it('is required only when at least one choice must be made', () => {
      expect(isRequired({ minSelect: 0, maxSelect: null })).toBe(false);
      expect(isRequired({ minSelect: 0, maxSelect: 5 })).toBe(false);
      expect(isRequired({ minSelect: 1, maxSelect: 1 })).toBe(true);
      expect(isRequired({ minSelect: 2, maxSelect: null })).toBe(true);
    });
  });

  describe('summariseRule', () => {
    it('leads with whether the customer can skip it', () => {
      expect(summariseRule({ minSelect: 0, maxSelect: null })).toBe(
        'Optional · Choose as many as you like',
      );
      expect(summariseRule({ minSelect: 1, maxSelect: 1 })).toBe('Required · Choose 1');
    });
  });

  describe('presetFor', () => {
    it('recognises each preset from the numbers it stores', () => {
      for (const preset of RULE_PRESETS) {
        if (preset.rule) {
          expect(presetFor(preset.rule)).toBe(preset.id);
        }
      }
    });

    it('falls back to custom for a rule no preset covers', () => {
      expect(presetFor({ minSelect: 2, maxSelect: 4 })).toBe('custom');
      expect(presetFor({ minSelect: 0, maxSelect: 3 })).toBe('custom');
    });
  });

  describe('ruleFrom', () => {
    it('treats a missing maximum as no maximum', () => {
      expect(ruleFrom({ minSelect: 0, maxSelect: undefined })).toEqual({
        minSelect: 0,
        maxSelect: null,
      });
      expect(ruleFrom({ minSelect: 1, maxSelect: null })).toEqual({
        minSelect: 1,
        maxSelect: null,
      });
    });

    it('does not mistake a zero for a missing value', () => {
      // `?? ` over a falsy check would turn "at most zero" into "no maximum" — a group nobody
      // can pick from into a group anyone can pick everything from.
      expect(ruleFrom({ minSelect: 0, maxSelect: 0 }).maxSelect).toBe(0);
    });
  });

  describe('ruleIsValid', () => {
    it('accepts the presets', () => {
      for (const preset of RULE_PRESETS) {
        if (preset.rule) {
          expect(ruleIsValid(preset.rule)).toBe(true);
        }
      }
    });

    it('rejects what the database would reject', () => {
      // CK_OptionGroups_SelectRange, mirrored so the person gets a sentence not a SQL error.
      expect(ruleIsValid({ minSelect: 5, maxSelect: 2 })).toBe(false);
      expect(ruleIsValid({ minSelect: -1, maxSelect: null })).toBe(false);
      expect(ruleIsValid({ minSelect: 0, maxSelect: 0 })).toBe(false);
      expect(ruleIsValid({ minSelect: 1.5, maxSelect: null })).toBe(false);
    });
  });
});
