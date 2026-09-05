/*
 * Public API Surface of ui
 *
 * Screens and rules both applications need, in the same words. What lives here is what would
 * otherwise be copied — and a copy is where the two versions of a password rule quietly stop
 * agreeing, which is not hypothetical: they already had.
 */

export * from './lib/passwords/passwords';
export * from './lib/reset-password/reset-password';
