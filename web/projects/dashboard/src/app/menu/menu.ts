import { Component } from '@angular/core';
import { Placeholder } from '../common/placeholder';

/** Routed and guarded now; the editor itself is step 8. */
@Component({
  selector: 'app-menu',
  imports: [Placeholder],
  template: `
    <app-placeholder
      icon="restaurant_menu"
      title="Menu"
      note="Categories, items, option groups and photos. Built in the next step — the route and
            its permissions are in place so the shell can be navigated end to end."
    />
  `,
})
export class Menu {}
