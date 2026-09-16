import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { TaskRow } from '../../../core/workflow/workflow.models';
import { WorkflowService } from '../../../core/workflow/workflow.service';
import { TaskForm } from './task-form';

/**
 * Phase 48 (43 carried item #4).
 *
 * <p>The finding this pins is that <b>nothing called `updateTask`</b>: phase 43 recorded editing as
 * happening on the list, and that list's form is create-only. So the assertions that matter are the
 * dispatch ones — a form holding a record must PUT, a form holding none must POST — and the
 * prefill, because a form that opens empty over an existing task would blank every field the user
 * did not retype.</p>
 */
describe('TaskForm', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';
  const taskId = '22222222-2222-2222-2222-222222222222';
  const typeId = '33333333-3333-3333-3333-333333333333';
  const userId = '44444444-4444-4444-4444-444444444444';

  function task(overrides: Partial<TaskRow> = {}): TaskRow {
    return {
      id: taskId,
      title: 'Call the supplier',
      description: 'About the late delivery',
      dueDate: '2026-05-20',
      createdAt: '2026-05-04T09:00:00.0000000+00:00',
      taskTypeId: typeId,
      taskTypeName: 'Phone call',
      taskTypeColor: '#123456',
      priority: 'Urgent',
      status: 'Pending',
      isPrivate: true,
      createdByUserId: userId,
      createdByName: 'Asha Sharma',
      assignedToUserId: userId,
      assignedToName: 'Asha Sharma',
      ...overrides,
    };
  }

  function mount(row: TaskRow | null) {
    const created: unknown[] = [];
    const updated: { id: string; request: Record<string, unknown> }[] = [];

    const workflow = {
      createTask: (_org: string, request: unknown): Observable<unknown> => {
        created.push(request);
        return of({ id: taskId });
      },
      updateTask: (_org: string, id: string, request: Record<string, unknown>): Observable<void> => {
        updated.push({ id, request });
        return of(undefined);
      },
    };

    TestBed.configureTestingModule({
      imports: [TaskForm],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: WorkflowService, useValue: workflow },
        { provide: ConfigurationService, useValue: { listTaskTypes: () => of([{ id: typeId, name: 'Phone call' }]) } },
        {
          provide: OrganizationsService,
          useValue: { listMembers: () => of([{ userId, fullName: 'Asha Sharma' }]) },
        },
      ],
    });

    const fixture = TestBed.createComponent(TaskForm);
    fixture.componentRef.setInput('organizationId', organizationId);
    fixture.componentRef.setInput('task', row);
    fixture.componentRef.setInput('parentType', 'Organization');
    fixture.componentRef.setInput('parentId', organizationId);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const save = () => {
      element.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
      fixture.detectChanges();
    };

    return { fixture, element, created, updated, save };
  }

  afterEach(() => TestBed.resetTestingModule());

  function value(element: HTMLElement, id: string): string {
    return element.querySelector<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(`#${id}`)!.value;
  }

  it('opens empty when there is no record', () => {
    const { element } = mount(null);

    expect(value(element, 'task-form-title')).toBe('');
    expect(value(element, 'task-form-priority')).toBe('Normal');
  });

  it('prefills every field the update command carries', () => {
    // A blank field here is not cosmetic: the command replaces, so an unfilled control would clear
    // the stored value the first time anyone pressed Save.
    const { element } = mount(task());

    expect(value(element, 'task-form-title')).toBe('Call the supplier');
    expect(value(element, 'task-form-description')).toBe('About the late delivery');
    expect(value(element, 'task-form-type')).toBe(typeId);
    expect(value(element, 'task-form-priority')).toBe('Urgent');
    expect(value(element, 'task-form-assigned-to')).toBe(userId);
    expect(element.querySelector<HTMLInputElement>('#task-form-is-private')!.checked).toBe(true);
  });

  it('creates when it holds no record', () => {
    const { element, created, updated, save } = mount(null);

    const title = element.querySelector<HTMLInputElement>('#task-form-title')!;
    title.value = 'A new task';
    title.dispatchEvent(new Event('input'));

    const type = element.querySelector<HTMLSelectElement>('#task-form-type')!;
    type.value = typeId;
    type.dispatchEvent(new Event('change'));

    save();

    expect(updated.length).toBe(0);
    expect(created.length).toBe(1);
    expect(created[0]).toMatchObject({ title: 'A new task', taskTypeId: typeId, parentType: 'Organization' });
  });

  it('updates the record it holds, and never creates a second one', () => {
    const { created, updated, save } = mount(task());

    save();

    expect(created.length, 'editing must not POST a duplicate').toBe(0);
    expect(updated.length).toBe(1);
    expect(updated[0].id).toBe(taskId);
    expect(updated[0].request).toMatchObject({
      title: 'Call the supplier',
      taskTypeId: typeId,
      priority: 'Urgent',
      isPrivate: true,
      assignedToUserId: userId,
      dueDate: '2026-05-20',
    });
  });

  it('refuses to send a task with no title', () => {
    const { element, created, updated, save } = mount(task());

    const title = element.querySelector<HTMLInputElement>('#task-form-title')!;
    title.value = '';
    title.dispatchEvent(new Event('input'));

    save();

    expect(created.length).toBe(0);
    expect(updated.length).toBe(0);
  });

  it('gives every control an id derived from the prefix, so two hosts cannot collide', () => {
    // `id="isPrivate"` was literally duplicated between the Deal and Task list forms before this.
    const { fixture, element } = mount(null);

    fixture.componentRef.setInput('idPrefix', 'somewhere-else');
    fixture.detectChanges();

    expect(element.querySelector('#somewhere-else-title')).toBeTruthy();
    expect(element.querySelector('#somewhere-else-is-private')).toBeTruthy();
    expect(element.querySelector('#task-form-title')).toBeNull();
  });
});
