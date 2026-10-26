import { Injectable, signal } from '@angular/core';

const AM: Record<string, string> = {
  'Overview': 'ዋና ገጽ', 'Language catalog': 'የቋንቋዎች ዝርዝር', 'Audio collection': 'የድምፅ ስብስብ',
  'Transcription': 'ጽሑፍ ማድረግ', 'Translation': 'ትርጉም', 'Quality review': 'የጥራት ግምገማ',
  'Dataset export': 'የውሂብ ማውጣት', 'Administration': 'አስተዳደር', 'Home': 'መነሻ',
  'Contributor': 'ተሳታፊ', 'Administrator': 'አስተዳዳሪ', 'Agent / focal person': 'ኤጀንት / ተጠሪ',
  'Sign in as contributor': 'እንደ ተሳታፊ ይግቡ', 'Administrator sign-in': 'የአስተዳዳሪ መግቢያ',
  'Create contributor account': 'የተሳታፊ መለያ ይፍጠሩ', 'Demo only': 'ማሳያ ብቻ',
  'Ethiopian Languages Data Factory': 'የኢትዮጵያ ቋንቋዎች የውሂብ ፋብሪካ',
  'A home for every Ethiopian voice': 'ለእያንዳንዱ የኢትዮጵያ ድምፅ መድረክ',
  'Collect, transcribe, translate, and review speech together.': 'ንግግርን በጋራ እንሰብስብ፣ ወደ ጽሑፍ እንቀይር፣ እንተረጉም እና እንገምግም።',
  'Build your profile and contribute to language datasets.': 'መገለጫዎን ይፍጠሩና ለቋንቋ ውሂብ ስብስቦች አስተዋጽኦ ያድርጉ።',
  'Full name': 'ሙሉ ስም', 'Phone number': 'ስልክ ቁጥር', 'Password': 'የይለፍ ቃል',
  'Register': 'ይመዝገቡ', 'Continue': 'ቀጥል', 'Sign out': 'ውጣ',
  'My dashboard': 'የእኔ ዳሽቦርድ', 'Welcome back': 'እንኳን ደህና መጡ',
  'Start recording': 'ቀረጻ ይጀምሩ', 'My tasks': 'የእኔ ተግባራት',
  'My submissions': 'ያስገባኋቸው ሥራዎች', 'Awaiting review': 'ግምገማ በመጠባበቅ ላይ',
  'Approved': 'ጸድቋል', 'Returned': 'ተመልሷል', 'In progress': 'በሂደት ላይ',
  'Choose a project': 'ፕሮጀክት ይምረጡ', 'Speaker': 'ተናጋሪ', 'Before you record': 'ከመቅዳትዎ በፊት',
  'Amharic': 'አማርኛ', 'Afaan Oromo': 'አፋን ኦሮሞ', 'Tigrinya': 'ትግርኛ',
  'Somali': 'ሶማሊ', 'Afar': 'አፋር', 'Sidama': 'ሲዳማ', 'Wolaytta': 'ወላይታ',
  'Management dashboard': 'የአስተዳደር ዳሽቦርድ', 'Users': 'ተጠቃሚዎች',
  'Pending reviews': 'በመጠባበቅ ላይ ያሉ ግምገማዎች', 'Approved items': 'የጸደቁ ንጥሎች',
  'Returned items': 'የተመለሱ ንጥሎች', 'Status': 'ሁኔታ', 'Language': 'ቋንቋ',
  'Submit for review': 'ለግምገማ ያስገቡ', 'Save draft': 'ረቂቁን ያስቀምጡ',
  'Transcript': 'የንግግር ጽሑፍ', 'Notes': 'ማስታወሻዎች',
  'Agent workspace': 'የኤጀንት የሥራ ቦታ', 'Support a contributor team': 'የተሳታፊዎችን ቡድን ይደግፉ',
  'Agent sign-in': 'የኤጀንት መግቢያ', 'Review queues': 'የግምገማ ወረፋዎች',
  'Approve': 'አጽድቅ', 'Reject': 'አትቀበል', 'Return with notes': 'ከማስታወሻ ጋር መልስ',
  'Download JSON': 'JSON ያውርዱ',
  'Staff invitation code (if you have one)': 'የሰራተኛ የግብዣ ኮድ (ካለ)',
  'Sign in with your server account.': 'ወደ ሰርቨር መለያዎ ይግቡ።',
  'Preferred interface language': 'የመረጡት የመገናኛ ቋንቋ',
  'I agree to the Terms and Conditions and Privacy Notice.': 'የአጠቃቀም ውሎችንና የግላዊነት ማስታወሻውን እስማማለሁ።',
  'I understand and consent to the audio recording and use described for this project.': 'ለዚህ ፕሮጀክት የድምፅ ቀረጻና አጠቃቀም መረጃውን ተረድቻለሁ እና ፈቃደኛ ነኝ።',
  'Agent referral link or code': 'የኤጀንት ሪፈራል ሊንክ ወይም ኮድ',
  'Explore Ethiopian language collections.': 'የኢትዮጵያ ቋንቋ ስብስቦችን ይመልከቱ።',
  'Your progress': 'እድገትዎ', 'Active': 'ንቁ', 'Growing': 'በማደግ ላይ',
  'Open': 'ክፈት', 'Demo data': 'የማሳያ ውሂብ',
};

@Injectable({ providedIn: 'root' })
export class TranslateService {
  lang = signal<'am' | 'en'>('am');

  t(key: string): string {
    if (this.lang() === 'en') return key;
    return AM[key] ?? key;
  }

  setLang(l: 'am' | 'en') {
    this.lang.set(l);
  }
}
