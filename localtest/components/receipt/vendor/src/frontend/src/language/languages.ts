import { nb } from './texts/nb';
import { en } from './texts/en';
import { nn } from './texts/nn';

export const languageLookup: Record<string, Record<string, Record<string, string>>> = {
  'nb': nb,
  'nn': nn,
  'en': en,
};

export const getLanguageFromCode = (languageCode: string, defaultLang: string = 'nb'): Record<string, Record<string, string>> => {
  return languageLookup[languageCode] || languageLookup[defaultLang];
}
