import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslateService } from '../../services/translate.service';

@Component({ selector: 'app-prepare', standalone: true, imports: [RouterLink],
  template: `
  <div class="page-heading"><div><div class="eyebrow">RECORDING PREPARATION · 02</div>
  <h1>{{ tr.t('Before you record') }}</h1></div></div>
  <section class="workspace-card">
    <ul class="guide-list">
      @for (tip of tips; track tip) {
        <li><span>✓</span>{{ tip }}</li>
      }
    </ul>
    <div class="form-footer"><a routerLink="/record" class="primary-button">{{ tr.t('Continue') }} →</a></div>
  </section>` })
export class PrepareComponent {
  tips = ['Choose a quiet room.','Reduce background noise.','Keep the microphone at a consistent distance.',
    'Speak naturally and clearly.','Get consent from everyone recorded.'];
  constructor(public tr: TranslateService) {}
}
