import { SymlinkOperationStatus } from '../../enums/symlink-operation-status';
import { SymlinkSetupStep } from '../../enums/symlink-setup-step';
import { SymlinkSuggestionStatus } from '../../enums/symlink-suggestion-status';
import { FeedbackState } from '../../interfaces/general/feedback-state';
import { SymlinkSuggestionReason } from '../../interfaces/symlinks/symlink-suggestion';
import { TranslationKey } from '../../services/i18n/_i18n-key';

type Translate = (key: TranslationKey) => string;

const reasonLabelKeys: Record<string, TranslationKey> = {
  'identical-title': 'symlinks.reason.identicalTitle',
  'alternative-title': 'symlinks.reason.alternativeTitle',
  'generic-word-difference': 'symlinks.reason.genericWord',
  'release-year-match': 'symlinks.reason.sameYear',
  'release-year-differs': 'symlinks.reason.differentYear',
  'release-date-unknown': 'symlinks.reason.dateUnknown',
  'runtime-match': 'symlinks.reason.similarRuntime',
  'runtime-differs': 'symlinks.reason.differentRuntime',
  'runtime-unknown': 'symlinks.reason.runtimeUnknown',
};

export function suggestionReasonLabel(reason: SymlinkSuggestionReason, translate: Translate): string {
  const key = reasonLabelKeys[reason.code];
  return key ? translate(key) : reason.detail;
}

const suggestionStatusKeys: Record<SymlinkSuggestionStatus, TranslationKey> = {
  Pending: 'symlinks.suggestion.Pending',
  Rejected: 'symlinks.suggestion.Rejected',
  Creating: 'symlinks.suggestion.Creating',
  Completed: 'symlinks.suggestion.Completed',
  Stale: 'symlinks.suggestion.Stale',
  Conflict: 'symlinks.suggestion.Conflict',
  Failed: 'symlinks.suggestion.Failed',
};

const operationStatusKeys: Record<SymlinkOperationStatus, TranslationKey> = {
  Pending: 'symlinks.operation.Pending',
  Active: 'symlinks.operation.Active',
  Conflict: 'symlinks.operation.Conflict',
  DeletePending: 'symlinks.operation.DeletePending',
  CleanupPending: 'symlinks.operation.DeletePending',
  Updating: 'symlinks.operation.Updating',
};

const setupStepKeys: Record<SymlinkSetupStep, TranslationKey> = {
  LinkCreated: 'symlinks.step.LinkCreated',
  MonitoringDisabled: 'symlinks.step.MonitoringDisabled',
  RescanRequested: 'symlinks.step.RescanRequested',
  EpisodeAssigned: 'symlinks.step.EpisodeAssigned',
  MetadataMatched: 'symlinks.step.MetadataMatched',
  Renamed: 'symlinks.step.Renamed',
  Completed: 'symlinks.step.Completed',
};

export function suggestionStatusLabel(status: SymlinkSuggestionStatus, translate: Translate): string {
  return translate(suggestionStatusKeys[status]);
}

/**
 * Where the state of an offer belongs colour-wise. A pair that went stale is reported by its own warning
 * beside the row, so the state alone decides here and staleness does not colour a second pill.
 */
export function suggestionFeedbackState(status: SymlinkSuggestionStatus): FeedbackState {
  if (status === 'Completed' || status === 'Creating') return 'success';
  if (status === 'Conflict' || status === 'Failed' || status === 'Stale') return 'error';
  return null;
}

export function operationStatusLabel(status: SymlinkOperationStatus, translate: Translate): string {
  return translate(operationStatusKeys[status]);
}

export function operationFeedbackState(status: SymlinkOperationStatus): FeedbackState {
  if (status === 'Active') return 'success';
  return status === 'Conflict' ? 'error' : null;
}

/** The setup step only means something while the entry is still being set up. */
export function setupStepLabel(step: SymlinkSetupStep | null, translate: Translate): string | null {
  return step === null ? null : translate(setupStepKeys[step]);
}
