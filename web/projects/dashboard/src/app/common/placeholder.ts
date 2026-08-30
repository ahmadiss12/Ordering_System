import { Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

/**
 * A section that is routed and reachable but not built yet.
 *
 * It exists so the shell can be finished and navigated end to end before every screen behind it
 * is written. It says plainly that the section is unbuilt rather than showing an empty table or
 * a spinner that never resolves, either of which reads as a bug.
 */
@Component({
  selector: 'app-placeholder',
  imports: [MatIconModule],
  template: `
    <div class="placeholder">
      <mat-icon>{{ icon() }}</mat-icon>
      <h1>{{ title() }}</h1>
      <p>{{ note() }}</p>
    </div>
  `,
  styles: `
    .placeholder {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 0.5rem;
      max-width: 32rem;
      margin: 4rem auto;
      text-align: center;
      color: var(--mat-sys-on-surface-variant);
    }

    mat-icon {
      font-size: 3rem;
      width: 3rem;
      height: 3rem;
      color: var(--mat-sys-outline);
    }

    h1 {
      margin: 0;
      font: var(--mat-sys-headline-small);
      color: var(--mat-sys-on-surface);
    }

    p {
      margin: 0;
      font: var(--mat-sys-body-medium);
    }
  `,
})
export class Placeholder {
  readonly icon = input.required<string>();
  readonly title = input.required<string>();
  readonly note = input.required<string>();
}
