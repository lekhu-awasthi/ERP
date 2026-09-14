import { TabParent, hasSmsHistory, tabParentId, tabParentPath } from './tab-parent';

/**
 * Phase 43 (39 carried item #1) — `TabParent` gained two record kinds, Deal and Task.
 *
 * Worth its own spec because this one function decides the URL for four components (the attachment
 * list, the activity panel and both things they embed) across four parent kinds. A wrong prefix here
 * is not a type error: every call site keeps compiling and every request goes to a route that either
 * does not exist or belongs to a different record.
 */
describe('tabParent', () => {
  const contact: TabParent = { kind: 'Contact', contactId: 'c1' };
  const document: TabParent = { kind: 'Document', documentType: 'Invoice', documentId: 'd1' };
  const deal: TabParent = { kind: 'Deal', dealId: 'de1' };
  const task: TabParent = { kind: 'Task', taskId: 't1' };

  it('routes each parent kind to its own endpoint family', () => {
    expect(tabParentPath(contact)).toBe('contacts/c1');
    expect(tabParentPath(document)).toBe('documents/Invoice/d1');
    expect(tabParentPath(deal)).toBe('deals/de1');
    expect(tabParentPath(task)).toBe('tasks/t1');
  });

  it('gives every parent kind a distinct path, which is the failure this guards', () => {
    const paths = [contact, document, deal, task].map(tabParentPath);
    expect(new Set(paths).size).toBe(paths.length);
  });

  it('reads the id of every parent kind', () => {
    expect(tabParentId(contact)).toBe('c1');
    expect(tabParentId(document)).toBe('d1');
    expect(tabParentId(deal)).toBe('de1');
    expect(tabParentId(task)).toBe('t1');
  });

  it('gives SMS history to a Contact only — the records have no phone number of their own', () => {
    expect(hasSmsHistory(contact)).toBe(true);
    expect(hasSmsHistory(deal)).toBe(false);
    expect(hasSmsHistory(task)).toBe(false);
    expect(hasSmsHistory(document)).toBe(false);
  });
});
