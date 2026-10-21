import { FocusKeeper } from './focus-keeper';

/** MutationObserver callbacks are microtasks; one turn of the event loop has run them all. */
const settle = () => new Promise((resolve) => setTimeout(resolve));

describe('FocusKeeper (phase 95)', () => {
  let keeper: FocusKeeper;
  let host: HTMLElement;

  beforeEach(() => {
    host = document.createElement('div');
    host.innerHTML = `
      <input id="before" />
      <table><tbody id="rows">
        <tr id="r1"><td><input id="q1" /></td><td><button aria-label="Remove line" id="x1">x</button></td></tr>
        <!--anchor-->
        <tr id="r2"><td><input id="q2" /></td><td><button aria-label="Remove line" id="x2">x</button></td></tr>
        <tr id="r3"><td><input id="q3" /></td><td><button aria-label="Remove line" id="x3">x</button></td></tr>
      </tbody></table>
      <button id="add">Add Line</button>
      <button id="save">Save</button>`;
    document.body.appendChild(host);
    keeper = new FocusKeeper();
    keeper.start(document);
  });

  afterEach(() => {
    keeper.ngOnDestroy();
    host.remove();
  });

  const byId = (id: string) => document.getElementById(id) as HTMLElement;

  /** What a browser does when the focused control is disabled: focus falls to the body. jsdom ignores blur() on a
   * disabled control, so the fall comes first here; the keeper sees the same thing either way. */
  const disable = (element: HTMLElement) => {
    element.blur();
    element.setAttribute('disabled', '');
  };

  it('gives a Save button its focus back when a failed save enables it again', async () => {
    const save = byId('save');
    save.focus();
    disable(save);
    await settle();
    expect(document.activeElement).toBe(document.body);

    save.removeAttribute('disabled');
    await settle();

    expect(document.activeElement).toBe(save);
  });

  it('leaves focus where the screen put it while the button was disabled (an error, a field)', async () => {
    const save = byId('save');
    save.focus();
    disable(save);
    await settle();
    byId('q1').focus();

    save.removeAttribute('disabled');
    await settle();

    expect(document.activeElement).toBe(byId('q1'));
  });

  it('moves a removed line\'s focus to the next line\'s Remove, past Angular\'s comment anchors', async () => {
    byId('x1').focus();
    byId('r1').remove();
    await settle();

    expect(document.activeElement).toBe(byId('x2'));
  });

  it('moves to the previous line\'s Remove when the last line is removed', async () => {
    byId('x3').focus();
    byId('r3').remove();
    await settle();

    expect(document.activeElement).toBe(byId('x2'));
  });

  it('moves to the list’s Add button when no line is left, even one above the table', async () => {
    const add = byId('add');
    host.insertBefore(add, host.querySelector('table'));
    for (const id of ['r1', 'r2']) byId(id).remove();
    await settle();
    byId('x3').focus();
    byId('r3').remove();
    await settle();

    expect(document.activeElement).toBe(add);
  });

  it('moves to the control after the table when no line is left and nothing offers to add one', async () => {
    byId('add').textContent = 'Recalculate';
    for (const id of ['r1', 'r2']) byId(id).remove();
    await settle();
    byId('x3').focus();
    byId('r3').remove();
    await settle();

    expect(document.activeElement).toBe(byId('add'));
  });

  it('does nothing when the screen moved focus itself, or when what left was not focused', async () => {
    byId('x2').focus();
    byId('r2').remove();
    byId('before').focus();
    await settle();
    expect(document.activeElement).toBe(byId('before'));

    byId('r1').remove();
    await settle();
    expect(document.activeElement).toBe(byId('before'));
  });
});
