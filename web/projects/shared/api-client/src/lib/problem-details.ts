import { ApiException, ProblemDetails } from './api-client';

/**
 * The `errors` extension the API adds to a 400, keyed by field name because one field can fail
 * several rules.
 *
 * `ProblemDetails` itself is generated and carries an index signature, so this only names the
 * one extension worth reading rather than redeclaring the type.
 */
export type ValidationErrors = Readonly<Record<string, readonly string[]>>;

/**
 * Turns whatever was thrown into a sentence worth showing someone.
 *
 * The order matters. A validation message names the field the person got wrong and is the most
 * useful thing available; `detail` is the domain's own words and is safe to show, because
 * DomainExceptionHandler already replaced anything internal with a generic line; `title` is the
 * last resort before the caller's fallback.
 */
export function describeError(error: unknown, fallback: string): string {
  const problem = problemFrom(error);
  if (!problem) {
    return fallback;
  }

  const messages = Object.values(validationErrorsOf(problem) ?? {})
    .flat()
    .filter((message) => typeof message === 'string' && message.length > 0);

  if (messages.length > 0) {
    return messages.join(' ');
  }

  return problem.detail ?? problem.title ?? fallback;
}

/** The HTTP status, when the failure came from the API at all. */
export function statusOf(error: unknown): number | null {
  return ApiException.isApiException(error) ? error.status : null;
}

/** Per-field validation messages, when the failure was a 400 carrying them. */
export function validationErrorsOf(problem: ProblemDetails | null): ValidationErrors | null {
  const errors: unknown = problem?.['errors'];
  return typeof errors === 'object' && errors !== null ? (errors as ValidationErrors) : null;
}

export function problemFrom(error: unknown): ProblemDetails | null {
  if (!ApiException.isApiException(error) || !error.response) {
    return null;
  }

  try {
    const parsed: unknown = JSON.parse(error.response);
    return typeof parsed === 'object' && parsed !== null ? (parsed as ProblemDetails) : null;
  } catch {
    // A gateway or proxy failing in front of the API returns HTML, not ProblemDetails. Showing
    // a fragment of that to a restaurant owner would be worse than the caller's own wording.
    return null;
  }
}
