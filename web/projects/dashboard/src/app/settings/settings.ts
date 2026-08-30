import { Component } from '@angular/core';
import { Placeholder } from '../common/placeholder';

/**
 * Owner-only. Present now mainly so the role split is real and testable: a staff account never
 * sees this in the sidenav, and is turned away by roleGuard if it reaches the URL directly.
 */
@Component({
  selector: 'app-settings',
  imports: [Placeholder],
  template: `
    <app-placeholder
      icon="settings"
      title="Settings"
      note="Opening hours, delivery zones, fees and prep time — the owner-only areas. Comes with
            the phases that add those endpoints."
    />
  `,
})
export class Settings {}
