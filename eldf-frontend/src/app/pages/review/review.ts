import { Component } from '@angular/core';
import { TranslateService } from '../../services/translate.service';

@Component({ selector: 'app-review', standalone: true, imports: [], templateUrl: './review.html' })
export class ReviewComponent {
  tab: 'All'|'Audio'|'Transcripts'|'Translations' = 'All';
  tabs: Array<'All'|'Audio'|'Transcripts'|'Translations'> = ['All','Audio','Transcripts','Translations'];
  constructor(public tr: TranslateService) {}
  setTab(t: string) { this.tab = t as typeof this.tab; }
}
