import { ChangeDetectionStrategy, Component, computed, inject, OnInit, Signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { SymlinkLink } from '../../interfaces/symlinks/symlink-link';
import { SymlinkRejection } from '../../interfaces/symlinks/symlink-decisions';
import { SymlinkMatchingRun, SymlinkSuggestion, SymlinkSuggestionAlternative } from '../../interfaces/symlinks/symlink-suggestion';
import { FeedbackState } from '../../interfaces/general/feedback-state';
import { AppDatePipe } from '../../pipes/app-date.pipe';
import { AppI18nService } from '../../services/app-i18n.service';
import { SymlinkActionState, SymlinkService } from '../../services/symlink.service';
import { TranslationKey } from '../../services/i18n/_i18n-key';
import { LoadingErrorSectionWidget } from '../../widgets/sections/loading-error-section/loading-error-section.widget';
import { LoadingSectionWidget } from '../../widgets/sections/loading-section/loading-section.widget';
import { ConfirmSymlinkDialogComponent, ConfirmSymlinkDialogData } from './confirm-symlink-dialog/confirm-symlink-dialog.component';
import { RejectSymlinkDialogComponent, RejectSymlinkDialogData } from './reject-symlink-dialog/reject-symlink-dialog.component';
import {
  operationFeedbackState,
  operationStatusLabel,
  setupStepLabel,
  suggestionFeedbackState,
  suggestionStatusLabel,
} from './symlinks.util';

/**
 * The three lists this feature is made of: pairs that were found, links that were agreed to, and the pairs
 * somebody declined. They share a screen because they answer each other — a declined pair leaves the first
 * list and appears in the third, and confirming moves a pair from the first into the second.
 */
@Component({
  selector: 'app-symlinks-page',
  imports: [MatButtonModule, RouterLink, AppDatePipe, LoadingErrorSectionWidget, LoadingSectionWidget],
  templateUrl: './symlinks.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './symlinks.page.scss',
})
export class SymlinksPage implements OnInit {
  private readonly i18n = inject(AppI18nService);
  private readonly dialog = inject(MatDialog);
  protected readonly symlinks = inject(SymlinkService);
  private readonly translate = (key: TranslationKey): string => this.i18n.t(key);

  protected readonly title: Signal<string> = computed((): string => this.i18n.t('symlinks.title'));
  protected readonly description: Signal<string> = computed((): string => this.i18n.t('symlinks.description'));
  protected readonly suggestionsTitle: Signal<string> = computed((): string => this.i18n.t('symlinks.suggestions.title'));
  protected readonly linksTitle: Signal<string> = computed((): string => this.i18n.t('symlinks.links.title'));
  protected readonly rejectionsTitle: Signal<string> = computed((): string => this.i18n.t('symlinks.rejections.title'));
  protected readonly suggestionsEmpty: Signal<string> = computed((): string => this.i18n.t('symlinks.suggestions.empty'));
  protected readonly linksEmpty: Signal<string> = computed((): string => this.i18n.t('symlinks.links.empty'));
  protected readonly rejectionsEmpty: Signal<string> = computed((): string => this.i18n.t('symlinks.rejections.empty'));
  protected readonly refreshLabel: Signal<string> = computed((): string => this.i18n.t('symlinks.refresh'));
  protected readonly configureLabel: Signal<string> = computed((): string => this.i18n.t('symlinks.configure'));

  /**
   * A declined pair belongs to the archive, which is read from its own endpoint, so the same decision would
   * otherwise be listed twice — once where it can still be acted on and once where it can be revoked.
   */
  protected readonly offeredSuggestions: Signal<SymlinkSuggestion[]> = computed((): SymlinkSuggestion[] =>
    this.symlinks.suggestions().filter((suggestion) => suggestion.status !== 'Rejected'),
  );
  protected readonly links = this.symlinks.links;
  protected readonly rejections = this.symlinks.rejections;
  protected readonly loading = this.symlinks.overviewLoading;
  protected readonly loadError = this.symlinks.overviewError;
  protected readonly refreshing = this.symlinks.refreshing;
  protected readonly lastRun = this.symlinks.lastRun;
  protected readonly settingsFailed = this.symlinks.settingsError;
  /** The configuration revision a preview and a confirmation have to agree on. */
  protected readonly revision = computed((): number | null => this.symlinks.settings()?.revision ?? null);
  /** Without a storage area every preview would be refused for the reason the settings tab already states. */
  protected readonly needsConfiguration = computed((): boolean => (this.symlinks.settings()?.storageAreas.length ?? 0) === 0);

  protected t(key: TranslationKey, replace?: Record<string, string>): string {
    return this.i18n.t(key, replace);
  }

  ngOnInit(): void {
    void this.loadAsync();
  }

  protected async loadAsync(): Promise<void> {
    // The path configuration belongs here although only the settings tab edits it: it is what a preview is
    // drawn from and what a confirmation is checked against. Both reads report their own failure.
    try {
      await Promise.all([this.symlinks.loadOverview(), this.symlinks.loadSettings()]);
    } catch {
      // overviewError and settingsError carry the sentences to the sections they belong to.
    }
  }

  protected async refreshAsync(): Promise<void> {
    try {
      await this.symlinks.refreshSuggestions();
    } catch {
      // The pass reports itself through overviewError and leaves the lists that are already on screen.
    }
  }

  protected suggestionState(id: string): SymlinkActionState {
    return this.symlinks.suggestionStates()[id] ?? {};
  }

  protected linkState(id: string): SymlinkActionState {
    return this.symlinks.linkStates()[id] ?? {};
  }

  protected suggestionStatus(suggestion: SymlinkSuggestion): string {
    return suggestionStatusLabel(suggestion.status, this.translate);
  }

  protected suggestionStatusClass(suggestion: SymlinkSuggestion): string {
    return this.statusClass(suggestionFeedbackState(suggestion.status));
  }

  protected operationStatus(link: SymlinkLink): string {
    return operationStatusLabel(link.operationStatus, this.translate);
  }

  protected operationStatusClass(link: SymlinkLink): string {
    return this.statusClass(operationFeedbackState(link.operationStatus));
  }

  protected setupStep(link: SymlinkLink): string | null {
    return setupStepLabel(link.setupStep, this.translate);
  }

  protected episodeLabel(seasonNumber: number, episodeNumber: number): string {
    return this.t('symlinks.episodeNumber', { season: String(seasonNumber), episode: String(episodeNumber) });
  }

  /** A competing source without a sentence of its own is still worth naming by the movie it is. */
  protected alternativeLabel(alternative: SymlinkSuggestionAlternative): string {
    return alternative.detail ?? this.t('symlinks.suggestions.alternative', { movie: String(alternative.radarrMovieId) });
  }

  /** The archive row is the pair itself, so all five ids together are what names it. */
  protected rejectionKey(rejection: SymlinkRejection): string {
    return [
      rejection.radarrInstanceId,
      rejection.radarrMovieId,
      rejection.sonarrInstanceId,
      rejection.sonarrSeriesId,
      rejection.sonarrEpisodeId,
    ].join('-');
  }

  protected runSummary(run: SymlinkMatchingRun): string {
    const counts = {
      instances: String(run.instancesConsidered),
      created: String(run.created),
      updated: String(run.updated),
      stale: String(run.markedStale),
      skipped: String(run.skippedRejected),
    };
    return run.didWork ? this.t('symlinks.run.summary', counts) : this.t('symlinks.run.unchanged', counts);
  }

  protected confirmAsync(suggestion: SymlinkSuggestion): void {
    const configRevision = this.revision();
    if (configRevision === null) return;
    // The dialog performs the confirmation itself, because only it can show the preview the decision rests
    // on; the service then put the new entry into the register and re-read the lists.
    this.dialog.open(ConfirmSymlinkDialogComponent, {
      data: { suggestion, configRevision } satisfies ConfirmSymlinkDialogData,
      width: '480px',
      maxWidth: '95vw',
    });
  }

  protected async rejectAsync(suggestion: SymlinkSuggestion): Promise<void> {
    const detail: string | undefined = await firstValueFrom(
      this.dialog
        .open(RejectSymlinkDialogComponent, {
          data: { suggestion } satisfies RejectSymlinkDialogData,
          width: '480px',
          maxWidth: '95vw',
        })
        .afterClosed(),
    );
    // Closing without a sentence means the dialog was cancelled; an empty sentence is no reason to remember
    // a pair by, so the decline stays bare.
    if (detail === undefined) return;
    const reason: string = detail.trim();
    try {
      await this.symlinks.rejectSuggestion(suggestion.id, reason.length > 0 ? reason : null);
    } catch {
      // The row shows its own error, which is where a person looks for why nothing moved.
    }
  }

  /**
   * The look a person asks for on one row. It writes nothing of its own, so the only answer that has to
   * reach the screen is the row the check left behind — which the service already put back in the list.
   */
  protected async checkAsync(link: SymlinkLink): Promise<void> {
    if (this.linkState(link.id).busy) return;
    try {
      await this.symlinks.checkLink(link.id);
    } catch {
      // The row carries the sentence; a check that could not run is not a reason to clear the list.
    }
  }

  protected async setFollowUpAsync(link: SymlinkLink, autoFollowUp: boolean): Promise<void> {
    if (this.linkState(link.id).busy) return;
    try {
      await this.symlinks.setFollowUp(link.id, autoFollowUp, link.revision);
    } catch {
      // On a refused revision the service re-reads the register, so the next attempt carries the current one.
    }
  }

  protected async deleteAsync(link: SymlinkLink): Promise<void> {
    if (!globalThis.confirm(this.t('symlinks.links.delete.question'))) return;
    try {
      await this.symlinks.deleteLink(link.id, link.revision);
    } catch {
      // Reported on the row: a link that is still on disk keeps its entry until it is really gone.
    }
  }

  protected async reactivateAsync(rejection: SymlinkRejection): Promise<void> {
    try {
      await this.symlinks.reactivateRejection({
        radarrInstanceId: rejection.radarrInstanceId,
        radarrMovieId: rejection.radarrMovieId,
        sonarrInstanceId: rejection.sonarrInstanceId,
        sonarrSeriesId: rejection.sonarrSeriesId,
        sonarrEpisodeId: rejection.sonarrEpisodeId,
      });
    } catch (error) {
      // The archive has no row of its own to put a sentence on, so the section above reports it.
      this.symlinks.overviewError.set(error instanceof Error ? error.message : this.t('symlinks.actionError'));
    }
  }

  private statusClass(state: FeedbackState): string {
    if (state === 'success') return 'status-pill is-success';
    if (state === 'error') return 'status-pill is-error';
    return 'status-pill';
  }
}
