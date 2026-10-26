import { Component } from '@angular/core';
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
  languages = ['Amharic','Afaan Oromo','Tigrinya','Somali','Afar','Sidama','Wolaytta'];
  constructor(public tr: TranslateService, public auth: AuthService) {}
}
