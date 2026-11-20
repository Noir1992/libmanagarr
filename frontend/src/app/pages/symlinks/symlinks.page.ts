import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, OnInit, Signal, signal } from '@angular/core';
import { form, FormField } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatInputModule } from '@angular/material/input';
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
import { LibrarySortDirection } from '../../enums/library-sort-direction';
import { LibrarySortField } from '../../enums/library-sort-field';
import { sortLibraryEntries } from '../../utils/library-sort-utils';
import { SortControlWidget, SortFieldOption } from '../../widgets/sort-control/sort-control.widget';
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
  imports: [
    FormField,
    MatButtonModule,
    MatInputModule,
    PageFilterWidget,
    SortControlWidget,
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
  private static readonly SORT_KEY = 'sort:symlinks';
  private static readonly SECTIONS_KEY = 'expanded:symlinks';
  protected readonly expandedSections = signal({ suggestions: true, links: false, rejections: false });
  protected readonly sortField = signal(LibrarySortField.ALPHABETICAL);
  protected readonly sortDirection = signal(LibrarySortDirection.ASCENDING);
  protected readonly sortOptions = computed<readonly SortFieldOption[]>(() => [
    { value: LibrarySortField.ALPHABETICAL, label: this.t('sort.field.alphabetical') },
    { value: LibrarySortField.ADDED, label: this.t('sort.field.created') },
  ]);
  protected readonly expandedLinks = signal<ReadonlySet<string>>(new Set());

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
      // The choices are static, so a status a previous version stored — such as the retired
      // WaitingForFile — is dropped to "all" instead of hiding every offer forever.
      validateRestoredValue: true,
      options: [
        { value: '', label: this.t('symlinks.all') },
        ...['Pending', 'Stale', 'Failed', 'Active', 'Updating', 'Conflict', 'DeletePending'].map((status) => ({
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
    this.sortRows(
      this.symlinks
        .suggestions()
        .filter(
          (suggestion) =>
            ['Pending', 'Stale', 'Failed', 'Conflict'].includes(suggestion.status) &&
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
    ),
  );
  protected readonly links = computed(() =>
    this.sortRows(
      this.symlinks.links().filter((link) =>
        // An in-progress deletion stays in the current list when its status changes.
        this.matches(link, this.isDeleting(link) ? this.filtersModel().status : link.operationStatus),
      ),
    ),
  );
  protected readonly rejections = computed(() => this.sortRows(this.symlinks.rejections()));
  protected readonly loading = this.symlinks.overviewLoading;
  protected readonly loadError = this.symlinks.overviewError;
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

  protected onSortFieldChanged(field: LibrarySortField): void {
    this.sortField.set(field);
    this.persistSort();
  }

  protected onSortDirectionChanged(direction: LibrarySortDirection): void {
    this.sortDirection.set(direction);
    this.persistSort();
  }

  private persistSort(): void {
    globalThis.localStorage.setItem(SymlinksPage.SORT_KEY, `${this.sortField()}:${this.sortDirection()}`);
  }

  private sortRows<T extends { radarrMovieTitle: string | null; sonarrSeriesTitle: string | null; createdAt: string }>(
    rows: readonly T[],
  ): T[] {
    return sortLibraryEntries(
      rows,
      (row) => ({ title: row.radarrMovieTitle ?? row.sonarrSeriesTitle ?? '', addedAt: row.createdAt }),
      this.sortField(),
      this.sortDirection(),
    );
  }

  protected toggleLinkDetails(id: string): void {
    this.expandedLinks.update((expanded) => {
      const next = new Set(expanded);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  protected toggleSection(section: 'suggestions' | 'links' | 'rejections'): void {
    this.expandedSections.update((sections) => ({ ...sections, [section]: !sections[section] }));
    globalThis.localStorage.setItem(SymlinksPage.SECTIONS_KEY, JSON.stringify(this.expandedSections()));
  }

  private restoreSections(): void {
    try {
      const saved: unknown = JSON.parse(globalThis.localStorage.getItem(SymlinksPage.SECTIONS_KEY) ?? 'null');
      if (saved && typeof saved === 'object') {
        const restored = { ...this.expandedSections() };
        for (const key of ['suggestions', 'links', 'rejections'] as const) {
          const value = (saved as Record<string, unknown>)[key];
          if (typeof value === 'boolean') restored[key] = value;
        }
        this.expandedSections.set(restored);
      }
    } catch {
      // Invalid stored state falls back to Suggestions open and the other sections closed.
    }
    globalThis.localStorage.setItem(SymlinksPage.SECTIONS_KEY, JSON.stringify(this.expandedSections()));
  }

  ngOnInit(): void {
    const [field, direction] = (globalThis.localStorage.getItem(SymlinksPage.SORT_KEY) ?? '').split(':');
    if (field === LibrarySortField.ALPHABETICAL || field === LibrarySortField.ADDED) this.sortField.set(field);
    if (direction === LibrarySortDirection.ASCENDING || direction === LibrarySortDirection.DESCENDING) this.sortDirection.set(direction);
    this.persistSort();
    this.restoreSections();
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

  protected needsRepair(link: SymlinkLink): boolean {
    return (
      link.operationStatus === 'Conflict' ||
      !!this.linkState(link.id).error ||
      link.findings.some((finding) => !['renaming-disabled', 'source-replaced', 'restored-unchecked'].includes(finding.code))
    );
  }

  protected async repairAsync(link: SymlinkLink): Promise<void> {
    if (this.linkLocked(link)) return;
    try {
      await this.symlinks.retryLink(link.id, link.revision);
    } catch {
      // The action state carries the refusal; foreign files remain untouched.
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
