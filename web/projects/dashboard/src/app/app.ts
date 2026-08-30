import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

/** The shell. Everything visible lives in a routed component. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  template: '<router-outlet />',
})
export class App {}
