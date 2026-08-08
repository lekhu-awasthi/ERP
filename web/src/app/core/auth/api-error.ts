import { HttpErrorResponse } from '@angular/common/http';

interface ProblemDetailsBody {
  title?: string;
  errors?: Record<string, string[]>;
}

/** Unwraps the ProblemDetails/HttpValidationProblemDetails body the Api's exception handler returns. */
export function extractErrorMessage(error: unknown): string | null {
  if (!(error instanceof HttpErrorResponse)) {
    return null;
  }

  const body = error.error as ProblemDetailsBody | undefined;

  if (body?.errors) {
    return Object.values(body.errors).flat().join(' ');
  }

  return body?.title ?? null;
}
