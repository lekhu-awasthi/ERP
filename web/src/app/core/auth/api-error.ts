import { HttpErrorResponse } from '@angular/common/http';

interface ProblemDetailsBody {
  title?: string;
  errors?: Record<string, string[]>;
  warningKind?: ApprovalWarningKind;
}

/**
 * Phase 31 -- which confirmable warning a 422 is. Three of them exist now (a stock shortfall, a
 * crossed credit limit, a negative cash balance), an approval can trip more than one, and each has
 * its own override flag -- so the client has to know which one it was shown rather than guessing
 * from the message text. Mirrors ExceptionHandling's `warningKind` ProblemDetails extension.
 */
export type ApprovalWarningKind = 'StockAvailability' | 'CreditLimit' | 'NegativeCashBalance';

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

/** The `warningKind` on a 422 ProblemDetails, or null for any other response. */
export function extractWarningKind(error: unknown): ApprovalWarningKind | null {
  if (!(error instanceof HttpErrorResponse) || error.status !== 422) {
    return null;
  }

  return (error.error as ProblemDetailsBody | undefined)?.warningKind ?? null;
}
