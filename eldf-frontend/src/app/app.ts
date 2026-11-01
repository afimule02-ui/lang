import { Component } from '@angular/core';
import { ShellComponent } from './components/shell/shell';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [ShellComponent],
  templateUrl: './app.html'
})
export class App {}
