import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

/**
 * Phase 42 -- the initial-bundle budget, and why it is the budget that moved rather than the bundle.
 *
 * Phase 34c left this open: 651 kB of initial bundle against a 500 kB budget, with "Bootstrap CSS and
 * the eager routes" named as the two things to look at. Measured here, both halves of that turned out
 * to be answered already:
 *
 *   * **There are no eager routes.** Every one of the 145 entries in `app.routes.ts` is a
 *     `loadComponent`, and the build emits 188+ lazy chunks. Phase 34c itself `@defer`red the shell.
 *     There is nothing left to move out of the initial bundle that a route could own.
 *   * **Nearly half the "bundle" is a stylesheet, and the budget counts it raw.** The initial total
 *     splits 314.60 kB of CSS against 336.83 kB of JS, and the CSS -- Bootstrap, which this app uses
 *     without Bootstrap's JavaScript -- compresses about 9:1. The figure a user's connection sees is
 *     the **128.39 kB** transfer size beside it.
 *
 * So a 500 kB raw budget was measuring something no user experiences, and failing on it every build
 * taught the team to ignore a warning. The budget is now 680 kB warn / 760 kB error: above today's
 * 651.44 kB by enough not to fire on a component, below it by little enough that eagerly importing a
 * charting or date library would trip it -- which is the regression a bundle budget is for.
 *
 * This spec is the pin. `angular.json` holds no prose, so the numbers live there and the reason lives
 * here; changing one without the other fails.
 */
describe('initial bundle budget', () => {
  const angularJson = JSON.parse(
    readFileSync(resolve(process.cwd(), 'angular.json'), 'utf8'),
  ) as {
    projects: Record<
      string,
      {
        architect: {
          build: {
            configurations: Record<string, { budgets?: { type: string; maximumWarning?: string; maximumError?: string }[] }>;
          };
        };
      }
    >;
  };

  const budgets = angularJson.projects['web'].architect.build.configurations['production'].budgets ?? [];

  it('is stated in the numbers phase 42 measured', () => {
    // Non-empty first: a guard whose input is missing passes vacuously (phase 34a).
    expect(budgets.length).toBeGreaterThan(0);

    const initial = budgets.find((b) => b.type === 'initial');
    expect(initial).toBeDefined();
    expect(initial!.maximumWarning).toBe('680kB');
    expect(initial!.maximumError).toBe('760kB');
  });

  it('still caps a single component stylesheet, which is the budget that never needed moving', () => {
    const componentStyle = budgets.find((b) => b.type === 'anyComponentStyle');
    expect(componentStyle).toBeDefined();
    expect(componentStyle!.maximumWarning).toBe('4kB');
    expect(componentStyle!.maximumError).toBe('8kB');
  });

  it('keeps every route lazy, which is what makes the initial bundle a constant', () => {
    // The half of the decision that is about the bundle rather than the budget: the initial bundle
    // stays the framework plus the stylesheet only while no route is imported eagerly. A `component:`
    // entry in the route table would put that component -- and everything it imports -- into it.
    const routes = readFileSync(resolve(process.cwd(), 'src/app/app.routes.ts'), 'utf8');
    expect(routes.length).toBeGreaterThan(0);

    expect(routes).not.toMatch(/^\s*component:\s/m);
    expect(routes.match(/loadComponent:/g)?.length ?? 0).toBeGreaterThan(100);
  });
});
