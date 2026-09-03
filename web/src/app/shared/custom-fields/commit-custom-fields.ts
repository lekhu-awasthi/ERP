import { Observable, OperatorFunction, catchError, map, of, switchMap } from 'rxjs';

import { extractErrorMessage } from '../../core/auth/api-error';
import { CustomFieldsEditor } from './custom-fields-editor';

/**
 * Phase 27a -- commits a document's custom-field values immediately after the document's own save
 * succeeds, as one operator a page can drop into its existing save pipeline.
 *
 * <p>Custom field values need their own request (the document id does not exist until Create
 * returns) but must look like part of the same Save click. Phase 20a wired that into two pages by
 * hand, nesting the commit inside each page's success handler and duplicating its error branch. Doing
 * that thirteen more times would have meant thirteen more hand-written nests, each a chance to drop
 * the error branch or the `saving.set(false)`.</p>
 *
 * <p><b>A failed commit never turns a successful save into an apparent failure.</b> The document
 * really was created or updated; reporting an outright error would invite the user to press Save
 * again and create a duplicate. So the commit error is reported through
 * <code>onCustomFieldError</code> and the original result is still emitted, letting the page's own
 * <code>next</code> handler navigate or reload exactly as it would have. That is the behaviour the
 * two Phase 20a pages hand-wrote; this is it in one place.</p>
 */
export function commitCustomFieldsThen<T>(
  editor: CustomFieldsEditor | undefined,
  documentIdOf: (result: T) => string,
  onCustomFieldError: (message: string) => void,
): OperatorFunction<T, T> {
  return (source: Observable<T>) =>
    source.pipe(
      switchMap((result) => {
        if (!editor) {
          return of(result);
        }

        return editor.commitTo(documentIdOf(result)).pipe(
          map(() => result),
          catchError((err: unknown) => {
            onCustomFieldError(
              extractErrorMessage(err) ?? 'Saved, but custom field values could not be saved.',
            );
            return of(result);
          }),
        );
      }),
    );
}
