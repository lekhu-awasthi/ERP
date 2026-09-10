/**
 * Minimal Node typings for the test build only.
 *
 * `a11y-sweep-guard.spec.ts` has to read `src/styles.scss` off disk to check that the stylesheet
 * still paints the colours `contrast-rules.ts` measures. Vite cannot hand it over: `.scss` is a
 * compiled asset, and both `?raw` and `?inline` return an **empty string** rather than an error --
 * which is worse than failing, because `toBeDefined()` passes and every assertion over the contents
 * becomes vacuously true.
 *
 * So the spec reads the file directly. This project has no `@types/node`, and pulling one in for a
 * single `readFileSync` is a dependency for a test; these two declarations are the whole surface the
 * spec uses. `tsconfig.app.json` excludes this directory, so the app build cannot see them and no
 * application file can accidentally start importing `node:fs` and still typecheck.
 */

declare module 'node:fs' {
  export function readFileSync(path: string, encoding: 'utf8'): string;
}

declare module 'node:path' {
  export function resolve(...segments: string[]): string;
}

declare const process: { cwd(): string };
