import { Component, OnInit, computed, inject, input, model, signal } from '@angular/core';

import { CustomTemplate } from '../../core/configuration/configuration.models';
import { ConfigurationService } from '../../core/configuration/configuration.service';

/**
 * Phase 27b -- the "+ Add Terms and Conditions" block, and `CustomTemplate`'s first real consumer
 * since Phase 20d created it (FR-11.3).
 *
 * <p><b>Shape confirmed live</b> on the reference tenant's Invoice add form (2026-09-03): a
 * collapsed "+ Add Terms and Conditions" link that expands into a <i>Select Template</i> dropdown
 * listing the tenant's Terms and Conditions templates, above an editor pre-filled with the chosen
 * template's body and freely editable from there. Choosing a template is therefore a
 * <b>starting point</b>, not a link: what the document stores is its own text.</p>
 *
 * <p><b>One deliberate divergence.</b> The reference product's editor is a rich-text box (bold,
 * lists, tables, images); this is a plain textarea, because `CustomTemplate.Body` has been plain
 * text since 20d and a WYSIWYG editor is the same kind of scope 20d declined when it descoped the
 * visual template designer. The mechanism -- pick a template, edit the text, store it on the
 * document -- is identical, and the seam to upgrade is this one component.</p>
 *
 * <p>Templates load once per host. A tenant with none still gets the textarea: terms typed by hand
 * are the common case on a new tenant, and hiding the field until a template exists would make the
 * feature look broken.</p>
 */
@Component({
  selector: 'app-terms-editor',
  imports: [],
  templateUrl: './terms-editor.html',
})
export class TermsEditor implements OnInit {
  private readonly configurationService = inject(ConfigurationService);

  readonly organizationId = input.required<string>();
  readonly disabled = input(false);

  /** Two-way: the host owns the value and sends it with the document's own save. */
  readonly terms = model<string>('');

  protected readonly templates = signal<CustomTemplate[]>([]);
  protected readonly expanded = signal(false);

  /** Expanded whenever there is already text to show -- an existing document's terms must never be
   * hidden behind a "+ Add" link the user has to know to click. */
  protected readonly isOpen = computed(() => this.expanded() || this.terms().length > 0);

  /** A required input cannot be read in the constructor, so the one lookup this control needs
   * happens here. */
  ngOnInit(): void {
    this.configurationService.listCustomTemplates(this.organizationId()).subscribe({
      next: (all) => this.templates.set(all.filter((x) => x.type === 'TermsAndConditions' && x.isActive)),
      // Non-fatal: the textarea works with no templates at all, and a document form must not fail
      // to open because a lookup call did.
      error: () => this.templates.set([]),
    });
  }

  protected open(): void {
    this.expanded.set(true);
  }

  protected onTermsInput(event: Event): void {
    this.terms.set((event.target as HTMLTextAreaElement).value);
  }

  /**
   * Replaces the text outright rather than appending. The dropdown is a "start from this" action,
   * and the reference product replaces too -- but this asks first when there is text to lose, since
   * silently discarding a paragraph someone typed is the one failure this control could cause.
   */
  protected onTemplateChange(event: Event): void {
    const select = event.target as HTMLSelectElement;
    const template = this.templates().find((x) => x.id === select.value);
    select.value = '';

    if (!template) {
      return;
    }

    if (this.terms().trim().length > 0 && !window.confirm('Replace the current terms with this template?')) {
      return;
    }

    this.terms.set(template.body);
  }
}
