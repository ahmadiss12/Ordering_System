import { FormControl, FormGroup, Validators } from '@angular/forms';
import {
  MAX_PASSWORD_LENGTH,
  MIN_PASSWORD_LENGTH,
  PASSWORD_HINT,
  confirmationMatches,
  hasLetterAndDigit,
  passwordValidators,
} from './passwords';

/**
 * The password rules, checked against the server's.
 *
 * <p>
 * The numbers are pinned deliberately. Nothing links this file to
 * <c>PasswordRules.ApplyPasswordRules</c> in the API — FluentValidation's rules are not in the
 * OpenAPI document, so the generated client cannot carry them — and the two had already drifted
 * once: every password form on the front end asked for eight characters and checked nothing
 * else, while the server has asked for ten with a letter and a digit since Phase 1. A test that
 * fails when somebody edits the number is the cheapest thing that makes the pair a decision.
 * </p>
 */
describe('password rules', () => {
  it('asks for exactly what the server asks for', () => {
    // If either of these changes, change AuthValidators.ApplyPasswordRules in the same commit.
    expect(MIN_PASSWORD_LENGTH).toBe(10);
    expect(MAX_PASSWORD_LENGTH).toBe(128);
  });

  it('says the rule in the hint, so nobody has to discover it by being refused', () => {
    expect(PASSWORD_HINT).toContain(String(MIN_PASSWORD_LENGTH));
    expect(PASSWORD_HINT.toLowerCase()).toContain('letter');
    expect(PASSWORD_HINT.toLowerCase()).toContain('number');
  });

  describe('hasLetterAndDigit', () => {
    it('accepts a password with both', () => {
      expect(hasLetterAndDigit(new FormControl('correct1horse'))).toBeNull();
    });

    it('names the letter as the missing half', () => {
      expect(hasLetterAndDigit(new FormControl('1234567890'))).toEqual({ needsLetter: true });
    });

    it('names the digit as the missing half', () => {
      // Ten letters passes a length check and is refused by the server. Saying "invalid" to
      // somebody who has typed ten characters is a guessing game.
      expect(hasLetterAndDigit(new FormControl('allletters'))).toEqual({ needsDigit: true });
    });

    it('leaves an empty box to the required validator', () => {
      // Otherwise an untouched field shows "add a letter" before anybody has typed anything.
      expect(hasLetterAndDigit(new FormControl(''))).toBeNull();
    });
  });

  describe('passwordValidators', () => {
    it('refuses everything the server would refuse', () => {
      expect(errorsFor('')).toContain('required');
      expect(errorsFor('short1')).toContain('minlength');
      expect(errorsFor('allletters')).toContain('needsDigit');
      expect(errorsFor('1234567890')).toContain('needsLetter');
      expect(errorsFor('a1'.repeat(MAX_PASSWORD_LENGTH))).toContain('maxlength');
    });

    it('accepts one the server would accept', () => {
      expect(errorsFor('correct1horse')).toEqual([]);
    });

    function errorsFor(value: string): string[] {
      const control = new FormControl(value, [...passwordValidators]);
      return Object.keys(control.errors ?? {});
    }
  });

  describe('confirmationMatches', () => {
    it('puts the mismatch on the confirmation box, where the message is drawn', () => {
      // Not on the group. Material draws a control's errors under that control, so a group-level
      // error has nowhere to appear — which is how the first version of this refused to submit
      // while showing nothing at all.
      const group = pair('correct1horse', 'correct1house');

      expect(group.controls.confirm.hasError('mismatch')).toBe(true);
      expect(group.errors).toBeNull();
      expect(group.invalid).toBe(true);
    });

    it('clears the mismatch once the two agree', () => {
      const group = pair('correct1horse', 'correct1house');
      group.controls.confirm.setValue('correct1horse');

      expect(group.controls.confirm.hasError('mismatch')).toBe(false);
      expect(group.valid).toBe(true);
    });

    it('does not flag an empty confirmation', () => {
      const group = pair('correct1horse', '');

      // Required still fails it, but "those two do not match" under a box nobody has typed in
      // reads as an accusation.
      expect(group.controls.confirm.hasError('mismatch')).toBe(false);
      expect(group.controls.confirm.hasError('required')).toBe(true);
    });

    it("keeps the confirmation box's other errors", () => {
      const group = pair('correct1horse', '');
      group.controls.confirm.setValue('x');
      group.controls.confirm.setValue('');

      expect(group.controls.confirm.hasError('required')).toBe(true);
    });

    function pair(
      password: string,
      confirm: string,
    ): FormGroup<{
      password: FormControl<string>;
      confirm: FormControl<string>;
    }> {
      return new FormGroup(
        {
          password: new FormControl(password, { nonNullable: true }),
          confirm: new FormControl(confirm, {
            nonNullable: true,
            validators: Validators.required,
          }),
        },
        { validators: confirmationMatches('password', 'confirm') },
      );
    }
  });
});
