export interface AfricanPhoneCountry { code: string; name: string; dialCode: string; }

export const AFRICAN_PHONE_COUNTRIES: AfricanPhoneCountry[] = [
  { code: 'DZ', name: 'Algeria', dialCode: '213' }, { code: 'AO', name: 'Angola', dialCode: '244' },
  { code: 'BJ', name: 'Benin', dialCode: '229' }, { code: 'BW', name: 'Botswana', dialCode: '267' },
  { code: 'BF', name: 'Burkina Faso', dialCode: '226' }, { code: 'BI', name: 'Burundi', dialCode: '257' },
  { code: 'CV', name: 'Cabo Verde', dialCode: '238' }, { code: 'CM', name: 'Cameroon', dialCode: '237' },
  { code: 'CF', name: 'Central African Republic', dialCode: '236' }, { code: 'TD', name: 'Chad', dialCode: '235' },
  { code: 'KM', name: 'Comoros', dialCode: '269' }, { code: 'CI', name: 'Côte d’Ivoire', dialCode: '225' },
  { code: 'CD', name: 'Democratic Republic of the Congo', dialCode: '243' }, { code: 'DJ', name: 'Djibouti', dialCode: '253' },
  { code: 'EG', name: 'Egypt', dialCode: '20' }, { code: 'GQ', name: 'Equatorial Guinea', dialCode: '240' },
  { code: 'ER', name: 'Eritrea', dialCode: '291' }, { code: 'SZ', name: 'Eswatini', dialCode: '268' },
  { code: 'ET', name: 'Ethiopia', dialCode: '251' }, { code: 'GA', name: 'Gabon', dialCode: '241' },
  { code: 'GM', name: 'Gambia', dialCode: '220' }, { code: 'GH', name: 'Ghana', dialCode: '233' },
  { code: 'GN', name: 'Guinea', dialCode: '224' }, { code: 'GW', name: 'Guinea-Bissau', dialCode: '245' },
  { code: 'KE', name: 'Kenya', dialCode: '254' }, { code: 'LS', name: 'Lesotho', dialCode: '266' },
  { code: 'LR', name: 'Liberia', dialCode: '231' }, { code: 'LY', name: 'Libya', dialCode: '218' },
  { code: 'MG', name: 'Madagascar', dialCode: '261' }, { code: 'MW', name: 'Malawi', dialCode: '265' },
  { code: 'ML', name: 'Mali', dialCode: '223' }, { code: 'MR', name: 'Mauritania', dialCode: '222' },
  { code: 'MU', name: 'Mauritius', dialCode: '230' }, { code: 'MA', name: 'Morocco', dialCode: '212' },
  { code: 'MZ', name: 'Mozambique', dialCode: '258' }, { code: 'NA', name: 'Namibia', dialCode: '264' },
  { code: 'NE', name: 'Niger', dialCode: '227' }, { code: 'NG', name: 'Nigeria', dialCode: '234' },
  { code: 'CG', name: 'Republic of the Congo', dialCode: '242' }, { code: 'RW', name: 'Rwanda', dialCode: '250' },
  { code: 'ST', name: 'Sao Tome and Principe', dialCode: '239' }, { code: 'SN', name: 'Senegal', dialCode: '221' },
  { code: 'SC', name: 'Seychelles', dialCode: '248' }, { code: 'SL', name: 'Sierra Leone', dialCode: '232' },
  { code: 'SO', name: 'Somalia', dialCode: '252' }, { code: 'ZA', name: 'South Africa', dialCode: '27' },
  { code: 'SS', name: 'South Sudan', dialCode: '211' }, { code: 'SD', name: 'Sudan', dialCode: '249' },
  { code: 'TZ', name: 'Tanzania', dialCode: '255' }, { code: 'TG', name: 'Togo', dialCode: '228' },
  { code: 'TN', name: 'Tunisia', dialCode: '216' }, { code: 'UG', name: 'Uganda', dialCode: '256' },
  { code: 'ZM', name: 'Zambia', dialCode: '260' }, { code: 'ZW', name: 'Zimbabwe', dialCode: '263' }
];

export function internationalPhoneNumber(value: string, dialCode: string): string {
  const trimmed = value.trim();
  const digits = trimmed.replace(/\D/g, '');
  const code = dialCode.replace(/\D/g, '');
  const hasInternationalPrefix = trimmed.startsWith('+') || trimmed.startsWith('00');
  const internationalDigits = trimmed.startsWith('00') ? digits.slice(2) : digits;

  if (hasInternationalPrefix) {
    const matchedCode = [code, ...AFRICAN_PHONE_COUNTRIES.map(country => country.dialCode)]
      .filter(Boolean)
      .sort((left, right) => right.length - left.length)
      .find(candidate => internationalDigits.startsWith(candidate));
    if (matchedCode) {
      const nationalNumber = internationalDigits.slice(matchedCode.length).replace(/^0+/, '');
      return `+${matchedCode}${nationalNumber}`;
    }
    return `+${internationalDigits}`;
  }

  const includesCountryCode = code && digits.startsWith(code) && digits.length > code.length + 6;
  const nationalNumber = (includesCountryCode ? digits.slice(code.length) : digits).replace(/^0+/, '');
  return `+${code}${nationalNumber}`;
}

export function isValidPhoneNumber(value: string, dialCode: string): boolean {
  const trimmed = value.trim();
  if (!trimmed || !/^(?:\+|00)?[\d\s().-]+$/.test(trimmed)) return false;

  const normalized = internationalPhoneNumber(trimmed, dialCode);
  return /^\+[1-9]\d{6,14}$/.test(normalized);
}
