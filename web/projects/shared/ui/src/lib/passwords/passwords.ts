import { AbstractControl, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';

/**
 * The password rules, in one place, matching the server's.
 *
 * <h4>Why this is shared rather than typed into each form</h4>
 *
 * Three screens ask somebody to choose a password — registration, the reset link, and the change
 * inside an account — and they live in two applications. Written separately they all said eight
 * characters and checked nothing else, while the server has asked for ten with a letter and a
 * digit since Phase 1. Every one of those forms would have accepted a password, sent it, and
 * shown a server validation message instead of its own.
 *
 * <h4>Keeping it matching</h4>
 *
 * The authority is <c>PasswordRules.ApplyPasswordRules</c> in
 * <c>api/src/OrderingSystem.Application/Features/Auth/AuthValidators.cs</c>. Nothing enforces the
 * pairing automatically — FluentValidation's rules are not in the OpenAPI document, so the
 * generated client cannot carry them — so the numbers below are pinned by a test, which makes
 * changing them a deliberate act rather than a drift.
 */
export const MIN_PASSWORD_LENGTH = 10;

/** Bounded because hashing cost scales with input, exactly as the server bounds it. */
export const MAX_PASSWORD_LENGTH = 128;

/** Said the same way on every screen that asks, so the rule reads as one rule. */
export const PASSWORD_HINT = `At least ${MIN_PASSWORD_LENGTH} characters, including a letter and a number.`;

/**
 * A letter and a digit, as two separate errors.
 *
 * Separate so the form can say which half is missing. "Invalid password" in front of somebody who
 * has typed ten letters is a guessing game.
 */
export const hasLetterAndDigit: ValidatorFn = (
  control: AbstractControl,
): ValidationErrors | null => {
  const value = String(control.value ?? '');
  if (!value) {
    return null;
  }

  const errors: ValidationErrors = {};
  if (!/[A-Za-z]/.test(value)) {
    errors['needsLetter'] = true;
  }
  if (!/[0-9]/.test(value)) {
    errors['needsDigit'] = true;
  }

  return Object.keys(errors).length ? errors : null;
};

/** Everything a new-password box must satisfy before it is worth sending. */
export const passwordValidators: readonly ValidatorFn[] = [
  Validators.required,
  Validators.minLength(MIN_PASSWORD_LENGTH),
  Validators.maxLength(MAX_PASSWORD_LENGTH),
  hasLetterAndDigit,
];

/**
 * A group validator that puts a mismatch on the confirmation box rather than on the group.
 *
 * <p>
 * The obvious version — a method on the component that compares the two values — was written
 * first and shipped nothing: Angular Material only draws a <c>mat-error</c> when the control
 * itself is in an error state, so the message never appeared and the button simply did nothing
 * when pressed. The error has to live on the control the message sits under.
 * </p>
 *
 * @param passwordKey the control holding the new password
 * @param confirmKey the control holding the repeat of it
 */
export function confirmationMatches(passwordKey: string, confirmKey: string): ValidatorFn {
  return (group: AbstractControl): null => {
    const password = group.get(passwordKey);
    const confirm = group.get(confirmKey);

    if (!password || !confirm) {
      return null;
    }

    // Only once there is something in the second box. Flagging a mismatch against an empty
    // confirmation would put an error under it before it has been typed in.
    const differ = String(confirm.value ?? '') !== '' && password.value !== confirm.value;
    const errors = { ...confirm.errors };

    if (differ) {
      confirm.setErrors({ ...errors, mismatch: true });
    } else if ('mismatch' in errors) {
      delete errors['mismatch'];
      confirm.setErrors(Object.keys(errors).length ? errors : null);
    }

    // Nothing on the group itself: an error here would have nowhere on the screen to appear.
    return null;
  };
}
