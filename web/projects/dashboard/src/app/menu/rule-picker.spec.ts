import { Component, signal } from '@angular/core';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { RulePicker } from './rule-picker';
import { SelectionRule } from './selection-rule';

@Component({
  imports: [RulePicker],
  template: `<app-rule-picker [(rule)]="rule" />`,
})
class Host {
  readonly rule = signal<SelectionRule>({ minSelect: 0, maxSelect: null });
}

/**
 * The control that keeps a restaurant owner away from two integers.
 */
describe('RulePicker', () => {
  let fixture: ComponentFixture<Host>;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [Host],
      providers: [provideZonelessChangeDetection(), provideNoopAnimations()],
    });

    fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    await fixture.whenStable();
  });

  afterEach(() => TestBed.resetTestingModule());

  async function choose(label: string): Promise<void> {
    const radios = [...fixture.nativeElement.querySelectorAll('mat-radio-button')];
    const target = radios.find((r) => (r as HTMLElement).textContent?.includes(label));
    (target as HTMLElement).querySelector('input')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  const preview = () =>
    (fixture.nativeElement.querySelector('.preview') as HTMLElement).textContent!.replace(
      /\s+/g,
      ' ',
    );

  it('shows the customer sentence for the chosen rule', async () => {
    await choose('Required — exactly one');

    expect(fixture.componentInstance.rule()).toEqual({ minSelect: 1, maxSelect: 1 });
    expect(preview()).toContain('Choose 1');
  });

  it('keeps the two one-option rules apart on screen', async () => {
    await choose('Optional — one at most');
    const optional = preview();

    await choose('Required — exactly one');
    const required = preview();

    // The whole reason the control exists: (0,1) and (1,1) must not look the same.
    expect(optional).not.toBe(required);
    expect(fixture.componentInstance.rule()).toEqual({ minSelect: 1, maxSelect: 1 });
  });

  it('reveals the numbers only under "Something else"', async () => {
    expect(fixture.nativeElement.querySelector('.numbers')).toBeNull();

    await choose('Something else');

    expect(fixture.nativeElement.querySelector('.numbers')).not.toBeNull();
  });

  it('stays on "Something else" while a custom rule is typed', async () => {
    await choose('Something else');

    // (0, null) matches a preset, so a picker that derived its state purely from the numbers
    // would snap back to "Optional — any number" and hide the fields mid-edit.
    expect(fixture.nativeElement.querySelector('.numbers')).not.toBeNull();
  });

  it('says so when the numbers cannot work', async () => {
    await choose('Something else');
    fixture.componentInstance.rule.set({ minSelect: 5, maxSelect: 2 });
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('.preview.invalid')).not.toBeNull();
    expect(preview()).toContain('cannot be larger');
  });
});
