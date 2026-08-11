export interface TreeRow<T> {
  item: T;
  depth: number;
}

/** Flattens a ParentId-linked list into depth-ordered rows for indented <select>/list rendering
 * -- the client-side stand-in for a server-side subtree query (see phase-3-status.md's scope
 * decisions: no ITreeQuery<T> yet, since a flat picker is all Phase 3 needs). */
export function buildTreeRows<T>(
  items: T[],
  getId: (item: T) => string,
  getParentId: (item: T) => string | null,
  getName: (item: T) => string,
): TreeRow<T>[] {
  const byParent = new Map<string | null, T[]>();
  for (const item of items) {
    const key = getParentId(item) ?? null;
    const siblings = byParent.get(key) ?? [];
    siblings.push(item);
    byParent.set(key, siblings);
  }

  const rows: TreeRow<T>[] = [];
  const visit = (parentId: string | null, depth: number): void => {
    const children = [...(byParent.get(parentId) ?? [])].sort((a, b) => getName(a).localeCompare(getName(b)));
    for (const child of children) {
      rows.push({ item: child, depth });
      visit(getId(child), depth + 1);
    }
  };
  visit(null, 0);

  return rows;
}
