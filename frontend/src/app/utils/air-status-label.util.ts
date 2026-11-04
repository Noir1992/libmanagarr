import { AppI18nService } from '../services/app-i18n.service';
import { TranslationKey } from '../services/i18n/_i18n-key';

/**
 * Sonarr and Radarr deliver the air status as a raw token whose casing differs per client
 * (`inCinemas` versus `incinemas`), so the token is matched case-insensitively here.
 */
const AIR_STATUS_LABEL_KEYS: Readonly<Record<string, TranslationKey>> = {
  announced: 'airStatus.announced',
  continuing: 'airStatus.continuing',
  deleted: 'airStatus.deleted',
  digitalrelease: 'airStatus.digitalRelease',
  ended: 'airStatus.ended',
  incinemas: 'airStatus.inCinemas',
  physicalrelease: 'airStatus.physicalRelease',
  released: 'airStatus.released',
  tba: 'airStatus.tba',
  unknown: 'filter.unknown',
  upcoming: 'airStatus.upcoming',
};

/** Translates an air status token into the user language, keeping unknown tokens readable as they are. */
export function airStatusLabel(status: string | undefined, i18n: AppI18nService): string {
  const normalized = status?.trim().toLowerCase();
  if (normalized === undefined || normalized.length === 0) return i18n.t('common.na');
  const key: TranslationKey | undefined = AIR_STATUS_LABEL_KEYS[normalized];

  return key !== undefined ? i18n.t(key) : (status as string);
}
