import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, OnInit, Signal, signal } from '@angular/core';
import { form, FormField } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatInputModule } from '@angular/material/input';
import { MAT_SLIDE_TOGGLE_DEFAULT_OPTIONS, MatSlideToggleModule } from '@angular/material/slide-toggle';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { SymlinkLink } from '../../interfaces/symlinks/symlink-link';
import { SymlinkRejection } from '../../interfaces/symlinks/symlink-decisions';
import {
  SymlinkSuggestion,
  SymlinkSuggestionAlternative,
  SymlinkSuggestionReason,
  ExistingEpisodeFileCheck,
} from '../../interfaces/symlinks/symlink-suggestion';
import { FeedbackState } from '../../interfaces/general/feedback-state';
import { FilterOptionType } from '../../enums/filter-option-type';
import { FilterOption } from '../../interfaces/filter-option';
import { PageFilterWidget } from '../../widgets/page-filter/page-filter.widget';
import { InfoIconWidget } from '../../widgets/info-icon/info-icon.widget';
import { FeedbackIconWidget } from '../../widgets/feedback-icon/feedback-icon.widget';
import { AppDatePipe } from '../../pipes/app-date.pipe';
import { AppI18nService } from '../../services/app-i18n.service';
import { SymlinkActionState, SymlinkService } from '../../services/symlink.service';
import { TranslationKey } from '../../services/i18n/_i18n-key';
import { LoadingErrorSectionWidget } from '../../widgets/sections/loading-error-section/loading-error-section.widget';
import { LoadingSectionWidget } from '../../widgets/sections/loading-section/loading-section.widget';
import { ConfirmSymlinkDialogComponent, ConfirmSymlinkDialogData } from './confirm-symlink-dialog/confirm-symlink-dialog.component';
import { RejectSymlinkDialogComponent, RejectSymlinkDialogData } from './reject-symlink-dialog/reject-symlink-dialog.component';
import { EditSymlinkDialogComponent } from './edit-symlink-dialog/edit-symlink-dialog.component';
import {
  operationFeedbackState,
  operationStatusLabel,
  suggestionFeedbackState,
  suggestionStatusLabel,
  suggestionReasonLabel,
} from './symlinks.util';

/**
 * The three lists this feature is made of: pairs that were found, links that were agreed to, and the pairs
 * somebody declined. They share a screen because they answer each other — a declined pair leaves the first
 * list and appears in the third, and confirming moves a pair from the first into the second.
 */
@Component({
  selector: 'app-symlinks-page',
  providers: [{ provide: MAT_SLIDE_TOGGLE_DEFAULT_OPTIONS, useValue: { disableToggleValue: true } }],
  imports: [
    FormField,
    MatButtonModule,
    MatInputModule,
    MatSlideToggleModule,
    PageFilterWidget,
    InfoIconWidget,
    FeedbackIconWidget,
    RouterLink,
    AppDatePipe,
    LoadingErrorSectionWidget,
    LoadingSectionWidget,
  ],
  templateUrl: './symlinks.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './symlinks.page.scss',
})
export class SymlinksPage implements OnInit {
  private readonly i18n = inject(AppI18nService);
  private readonly dialog = inject(MatDialog);
  private readonly destroy = inject(DestroyRef);
  protected readonly featureEnabled = computed(() => this.symlinks.settings()?.enabled !== false);
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
  protected readonly filtersModel = signal({ title: '', status: '', instance: '' });
  protected readonly searchFields = form(this.filtersModel);
  protected readonly instances = computed(() => [
    ...new Set([
      ...this.symlinks.suggestions().flatMap((row) => [row.radarrInstanceId, row.sonarrInstanceId]),
      ...this.symlinks.links().flatMap((row) => [row.radarrInstanceId, row.sonarrInstanceId]),
    ]),
  ]);
  protected readonly configureLabel: Signal<string> = computed((): string => this.i18n.t('symlinks.configure'));
  protected readonly filters: Signal<readonly FilterOption[]> = computed(() => [
    {
      id: 'status',
      label: this.t('symlinks.filterStatus'),
      type: FilterOptionType.SELECT,
      options: [
        { value: '', label: this.t('symlinks.all') },
        ...['Pending', 'WaitingForFile', 'Stale', 'Failed', 'Active', 'Updating', 'Conflict', 'DeletePending'].map((status) => ({
          value: status,
          label: this.filterStatusLabel(status),
        })),
      ],
      selectedValue: this.filtersModel().status,
      onChanged: (status: string): void => this.filtersModel.update((model) => ({ ...model, status })),
    },
    {
      id: 'instance',
      label: this.t('symlinks.filterInstance'),
      type: FilterOptionType.SELECT,
      options: [
        { value: '', label: this.t('symlinks.all') },
        ...this.instances().map((id) => ({ value: id, label: this.instanceLabel(id) })),
      ],
      selectedValue: this.filtersModel().instance,
      onChanged: (instance: string): void => this.filtersModel.update((model) => ({ ...model, instance })),
    },
  ]);

  /**
   * A declined pair belongs to the archive, which is read from its own endpoint, so the same decision would
   * otherwise be listed twice — once where it can still be acted on and once where it can be revoked.
   */
  protected readonly offeredSuggestions: Signal<SymlinkSuggestion[]> = computed((): SymlinkSuggestion[] =>
    this.symlinks
      .suggestions()
      .filter(
        (suggestion) =>
          ['Pending', 'WaitingForFile', 'Stale', 'Failed', 'Conflict'].includes(suggestion.status) &&
          this.matches(suggestion, suggestion.status) &&
          !this.symlinks
            .links()
            .some(
              (link) =>
                this.isDeleting(link) &&
                link.radarrInstanceId === suggestion.radarrInstanceId &&
                link.radarrMovieId === suggestion.radarrMovieId &&
                link.sonarrInstanceId === suggestion.sonarrInstanceId &&
                link.sonarrSeriesId === suggestion.sonarrSeriesId &&
                link.sonarrEpisodeId === suggestion.sonarrEpisodeId,
            ),
      ),
  );
  protected readonly links = computed(() =>
    this.symlinks.links().filter((link) =>
      // An in-progress deletion stays in the current list when its status changes.
      this.matches(link, this.isDeleting(link) ? this.filtersModel().status : link.operationStatus),
    ),
  );
  protected readonly rejections = this.symlinks.rejections;
  protected readonly loading = this.symlinks.overviewLoading;
  protected readonly loadError = this.symlinks.overviewError;
  protected readonly refreshing = this.symlinks.refreshing;
  protected readonly settingsFailed = this.symlinks.settingsError;
  /** The configuration revision a preview and a confirmation have to agree on. */
  protected readonly revision = computed((): number | null => this.symlinks.settings()?.revision ?? null);
  /** Creation requires loaded media settings; filesystem access is proven by the capability test. */
  protected readonly needsConfiguration = computed((): boolean => !this.symlinks.settings()?.mediaRoot);

  protected t(key: TranslationKey, replace?: Record<string, string>): string {
    return this.i18n.t(key, replace);
  }

  protected matchingReasons(suggestion: SymlinkSuggestion): SymlinkSuggestionReason[] {
    return suggestion.reasons.filter((reason) => reason.code !== 'episode-has-file');
  }

  protected reasonLabel(reason: SymlinkSuggestionReason): string {
    return suggestionReasonLabel(reason, this.translate);
  }

  protected reasonDetails(reasons: SymlinkSuggestionReason[]): string {
    return reasons.map((reason) => reason.detail).join(' ');
  }

  protected hasExistingEpisodeFile(suggestion: SymlinkSuggestion): boolean {
    return suggestion.reasons.some((reason) => reason.code === 'episode-has-file');
  }

  protected existingFileLabel(check: ExistingEpisodeFileCheck): string {
    const keys: Record<ExistingEpisodeFileCheck['status'], TranslationKey> = {
      Matches: 'symlinks.existingFile.Matches',
      TargetMismatch: 'symlinks.existingFile.TargetMismatch',
      BrokenSymlink: 'symlinks.existingFile.BrokenSymlink',
      RegularFile: 'symlinks.existingFile.RegularFile',
      MissingFile: 'symlinks.existingFile.MissingFile',
      Unavailable: 'symlinks.existingFile.Unavailable',
      NoFile: 'symlinks.existingFile.NoFile',
    };
    return this.t(keys[check.status]);
  }

  protected syncFilters(): void {
    // The shared widget restores persisted values directly on the filter definitions.
    const selected = (id: string): string => {
      const filter = this.filters().find((option) => option.id === id);
      return filter?.type === FilterOptionType.SELECT ? (filter.selectedValue ?? '') : '';
    };
    const status = selected('status');
    const instance = selected('instance');
    this.filtersModel.update((model) => (model.status === status && model.instance === instance ? model : { ...model, status, instance }));
  }

  ngOnInit(): void {
    void this.loadAsync();
    const timer = globalThis.setInterval(() => {
      if (
        !globalThis.document.hidden &&
        this.symlinks.links().some((link) => link.operationStatus !== 'Active' || link.setupStep !== 'Completed')
      )
        void this.symlinks.refreshProgress();
    }, 5000);
    this.destroy.onDestroy(() => globalThis.clearInterval(timer));
  }

  private matches(
    row: { radarrMovieTitle: string | null; sonarrSeriesTitle: string | null; radarrInstanceId: string; sonarrInstanceId: string },
    status: string,
  ): boolean {
    const filter = this.filtersModel();
    return (
      (!filter.title ||
        `${row.radarrMovieTitle ?? ''} ${row.sonarrSeriesTitle ?? ''}`.toLocaleLowerCase().includes(filter.title.toLocaleLowerCase())) &&
      (!filter.status || status === filter.status) &&
      (!filter.instance || row.radarrInstanceId === filter.instance || row.sonarrInstanceId === filter.instance)
    );
  }

  protected edit(link?: SymlinkLink): void {
    if (link && this.linkLocked(link)) return;
    this.dialog.open(EditSymlinkDialogComponent, { data: link, width: '760px', maxWidth: '95vw' });
  }

  protected instanceLabel(id: string): string {
    const rows = [...this.symlinks.links(), ...this.symlinks.suggestions()];
    return (
      rows.find((row) => row.radarrInstanceId === id)?.radarrInstanceName ??
      rows.find((row) => row.sonarrInstanceId === id)?.sonarrInstanceName ??
      id
    );
  }

  protected filterStatusLabel(status: string): string {
    const keys: Record<string, TranslationKey> = {
      Pending: 'symlinks.suggestion.Pending',
      WaitingForFile: 'symlinks.suggestion.WaitingForFile',
      Stale: 'symlinks.suggestion.Stale',
      Failed: 'symlinks.suggestion.Failed',
      Active: 'symlinks.operation.Active',
      Updating: 'symlinks.operation.Updating',
      Conflict: 'symlinks.operation.Conflict',
      DeletePending: 'symlinks.operation.DeletePending',
      CleanupPending: 'symlinks.operation.DeletePending',
    };
    return this.t(keys[status]);
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

  protected isDeleting(link: SymlinkLink): boolean {
    return (
      link.operationStatus === 'DeletePending' || link.operationStatus === 'CleanupPending' || this.linkState(link.id).deleting === true
    );
  }

  protected linkLocked(link: SymlinkLink): boolean {
    return !this.featureEnabled() || this.isDeleting(link) || this.linkState(link.id).busy === true;
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

  protected specialLabel(episodeNumber: number): string {
    return this.t('symlinks.specialNumber', { episode: String(episodeNumber) });
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
    if (this.linkLocked(link)) return;
    try {
      await this.symlinks.checkLink(link.id);
    } catch {
      // The row carries the sentence; a check that could not run is not a reason to clear the list.
    }
  }

  protected async setFollowUpAsync(link: SymlinkLink, autoFollowUp: boolean): Promise<void> {
    if (this.linkLocked(link)) return;
    try {
      await this.symlinks.setFollowUp(link.id, autoFollowUp, link.revision);
    } catch {
      // On a refused revision the service re-reads the register, so the next attempt carries the current one.
    }
  }

  protected async deleteAsync(link: SymlinkLink): Promise<void> {
    if (this.linkLocked(link)) return;
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
