import { PathReaderVerification } from '../../../enums/path-reader-verification';
import { PathReaderKind } from '../../../enums/path-reader-kind';
import { SymlinkCapabilityStatus } from '../../../enums/symlink-capability-status';
import { FeedbackState } from '../../../interfaces/general/feedback-state';
import { TranslationKey } from '../../../services/i18n/_i18n-key';

export type Translate = (key: TranslationKey) => string;

const capabilityStatusKeys: Record<SymlinkCapabilityStatus, TranslationKey> = {
  NotTested: 'settings.symlinks.capability.NotTested',
  Available: 'settings.symlinks.capability.Available',
  PermissionDenied: 'settings.symlinks.capability.PermissionDenied',
  Unsupported: 'settings.symlinks.capability.Unsupported',
  StorageUnavailable: 'settings.symlinks.capability.StorageUnavailable',
  Failed: 'settings.symlinks.capability.Failed',
};

const verificationKeys: Record<PathReaderVerification, TranslationKey> = {
  Unverified: 'settings.symlinks.verification.Unverified',
  Verified: 'settings.symlinks.verification.Verified',
  Failed: 'settings.symlinks.verification.Failed',
};

const readerKindKeys: Record<PathReaderKind, TranslationKey> = {
  Sonarr: 'settings.sonarr',
  MediaPlayer: 'settings.symlinks.reader.kind.MediaPlayer',
};

export function capabilityStatusLabel(status: SymlinkCapabilityStatus, translate: Translate): string {
  return translate(capabilityStatusKeys[status]);
}

/**
 * Where the answer belongs visually. A usable symlink counts as settled; an answer that was produced
 * under an older path configuration is shown neutral, because it says nothing about the paths in use.
 */
export function capabilityFeedbackState(status: SymlinkCapabilityStatus, stale: boolean): FeedbackState {
  if (status === 'NotTested' || stale) return null;
  return status === 'Available' ? 'success' : 'error';
}

export function verificationLabel(verification: PathReaderVerification, translate: Translate): string {
  return translate(verificationKeys[verification]);
}

export function readerKindLabel(kind: PathReaderKind, translate: Translate): string {
  return translate(readerKindKeys[kind]);
}

/**
 * Turns one validation key of the save answer into the path of its control, so the sentence appears at
 * the row that was refused instead of in a list nobody reads.
 */
export function controlPathFromValidationKey(key: string): string | null {
  const withoutPrefix = key.startsWith('symlinks.') ? key.slice('symlinks.'.length) : key;
  if (withoutPrefix.length === 0 || withoutPrefix === 'symlinks') return null;
  return withoutPrefix.replace(/\[(\d+)]/g, '.$1');
}
