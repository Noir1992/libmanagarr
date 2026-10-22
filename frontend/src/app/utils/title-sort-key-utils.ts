import { TitleSortingSettings } from '../interfaces/settings/title-sorting-settings';
import { foldTitleWord } from './unicode-simple-case-fold';

export { foldTitleWord } from './unicode-simple-case-fold';

export function isTitleWhitespace(value: string): boolean {
  const code = value.charCodeAt(0);
  return (
    (code >= 0x0009 && code <= 0x000d) ||
    code === 0x0020 ||
    code === 0x0085 ||
    code === 0x00a0 ||
    code === 0x1680 ||
    (code >= 0x2000 && code <= 0x200a) ||
    code === 0x2028 ||
    code === 0x2029 ||
    code === 0x202f ||
    code === 0x205f ||
    code === 0x3000
  );
}

export function trimTitleWhitespace(value: string): string {
  let start = 0;
  while (start < value.length && isTitleWhitespace(value[start])) start++;
  let end = value.length;
  while (end > start && isTitleWhitespace(value[end - 1])) end--;
  return value.slice(start, end);
}

export function normalizeLeadingWords(words: readonly string[]): string[] {
  const seen = new Set<string>();
  const normalized: string[] = [];
  for (const value of words) {
    const word = trimTitleWhitespace(value).normalize('NFC');
    const key = foldTitleWord(word);
    if (word && !seen.has(key)) {
      normalized.push(word);
      seen.add(key);
    }
  }
  return normalized;
}

export function buildTitleSortKey(title: string, settings: TitleSortingSettings): string {
  if (!settings.enabled || settings.ignoredLeadingWords.length === 0) return title;

  let start = 0;
  while (start < title.length && isTitleWhitespace(title[start])) start++;
  let end = start;
  while (end < title.length && !isTitleWhitespace(title[end])) end++;
  let rest = end;
  while (rest < title.length && isTitleWhitespace(title[rest])) rest++;
  if (start === end || rest === title.length) return title;

  const firstWord = foldTitleWord(title.slice(start, end));
  return settings.ignoredLeadingWords.some((word) => foldTitleWord(word) === firstWord) ? title.slice(rest) : title;
}
