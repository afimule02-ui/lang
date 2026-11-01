import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslateService } from '../../services/translate.service';
import { CaptureSessionService } from '../../services/capture-session.service';

@Component({ selector: 'app-prepare', standalone: true, imports: [FormsModule],
  template: `
  <div class="page-heading"><div><div class="eyebrow">RECORDING PREPARATION · 02</div>
  <h1>{{ tr.t('Before you record') }}</h1></div></div>
  <section class="workspace-card">
    <h2 class="topic-heading">{{ tr.t('Choose conversation topics') }}</h2>
    <p class="topic-help">{{ tr.t('Select one or more topics to guide your recording.') }}</p>
    <div class="topic-selection">
      @for (topic of topics; track topic) {
        <label class="topic-option">
          <input type="checkbox" [checked]="selectedTopics.includes(topic)" (change)="toggleTopic(topic, $event)" />
          <span>{{ tr.t(topic) }}</span>
        </label>
      }
    </div>
    @if (error) { <p class="form-error">{{ tr.t(error) }}</p> }
    <h2 class="topic-heading">{{ tr.t('Recording reminders') }}</h2>
    <ul class="guide-list">
      @for (tip of tips; track tip) { <li><span>✓</span>{{ tr.t(tip) }}</li> }
    </ul>
    <div class="form-footer"><button class="primary-button" (click)="continue()">{{ tr.t('Continue') }} →</button></div>
  </section>` })
export class PrepareComponent {
  topics = ['Family and daily life', 'Community and traditions', 'Education and learning', 'Work and livelihood',
    'Food and cooking', 'Festivals and celebrations', 'Nature and environment', 'History and local stories',
    'Health and wellbeing', 'Farming and animals', 'Travel and places', 'Language and cultural expressions',
    'Hopes for the future', 'Folktales and storytelling', 'Music, art and games'];
  selectedTopics: string[] = [];
  error = '';
  tips = ['Choose a quiet room.','Reduce background noise.','Keep the microphone at a consistent distance.',
    'Speak naturally and clearly.','Get consent from everyone recorded.'];

  constructor(public tr: TranslateService, private capture: CaptureSessionService, private router: Router) {
    this.selectedTopics = [...this.capture.topics];
  }

  toggleTopic(topic: string, event: Event) {
    const checked = (event.target as HTMLInputElement).checked;
    this.selectedTopics = checked
      ? [...this.selectedTopics, topic]
      : this.selectedTopics.filter(item => item !== topic);
  }

  continue() {
    if (!this.selectedTopics.length) { this.error = 'Select at least one topic before continuing.'; return; }
    this.capture.setTopics(this.selectedTopics);
    this.router.navigate(['/record']);
  }
}
