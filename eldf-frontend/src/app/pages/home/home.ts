import { Component } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { TranslateService } from '../../services/translate.service';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './home.html'
})
export class HomeComponent {
  vision: Record<'am' | 'en', string> = {
    am: 'የአፍሪካ ቋንቋ ሁሉ በዲጂታል ዓለም ድምፁ ይሰማ።',
    en: 'Every African language deserves a voice in the digital world.'
  };
  mission: Record<'am' | 'en', string> = {
    am: 'ከአፍሪካ ማኅበረሰቦች ጋር በመስራት ቋንቋዎችን እንሰበስባለን፣ እናስቀምጣለን፣ ለትምህርትና ለዲጂታል አጠቃቀም እናዳርሳለን።',
    en: "We work with African communities to collect, preserve, and share language data for learning and digital use."
  };

  constructor(public tr: TranslateService, public auth: AuthService, http: HttpClient) {
    http.get<{ vision?: Record<'am' | 'en', string>; mission?: Record<'am' | 'en', string> }>('/api/site-content')
      .subscribe({
        next: content => {
          if (content.vision?.am && content.vision.en && content.mission?.am && content.mission.en) {
            this.vision = content.vision;
            this.mission = content.mission;
          }
        },
        error: () => { /* Keep the built-in bilingual copy when the API is unavailable. */ }
      });
  }

  get lang(): 'am' | 'en' { return this.auth.lang(); }

  setLang(lang: 'am' | 'en') {
    this.auth.setLang(lang);
    this.tr.setLang(lang);
  }
}
