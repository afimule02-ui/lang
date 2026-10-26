import { Component } from '@angular/core';
import { TranslateService } from '../../services/translate.service';
import { AuthService } from '../../services/auth.service';

@Component({ selector: 'app-agent', standalone: true, imports: [], templateUrl: './agent.html' })
export class AgentComponent {
  constructor(public tr: TranslateService, public auth: AuthService) {}
  get user() { return this.auth.user(); }
}
