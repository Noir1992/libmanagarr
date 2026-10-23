import { AppLanguage } from '../interfaces/general/app-language';

export function detectBrowserLanguage(language: string = globalThis.navigator?.language ?? 'en'): AppLanguage {
  const base = language.toLowerCase().split('-')[0];
  return base === 'de' || base === 'fr' ? base : 'en';
}
