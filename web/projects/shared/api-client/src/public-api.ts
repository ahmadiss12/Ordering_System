/*
 * Public API surface of api-client.
 *
 * `api-client.ts` is GENERATED from the API's OpenAPI document by
 * scripts/generate-api-client.sh — do not edit it by hand. CI regenerates it and fails if the
 * committed copy has drifted, so a hand edit would be reverted by the next run anyway.
 *
 * The payoff, from ADR-14: a breaking change to the API becomes a TypeScript compile error in
 * every client, in the same commit, instead of an `undefined` a user finds later.
 */
export * from './lib/api-client';
export * from './lib/provide-api-client';
