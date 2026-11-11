import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { disabled, form, FormField } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { PreviewSymlinkLinkRequest, SymlinkChoices } from '../../../interfaces/symlinks/symlink-decisions';
import { SymlinkLink, SymlinkLinkPlan } from '../../../interfaces/symlinks/symlink-link';
import { AppI18nService } from '../../../services/app-i18n.service';
import { TranslationKey } from '../../../services/i18n/_i18n-key';
import { SymlinkService } from '../../../services/symlink.service';

function groupByInstance<T extends { instanceId: string; instanceName: string }>(choices: T[]) {
  const groups = new Map<string, { instanceId: string; instanceName: string; choices: T[] }>();
  for (const choice of choices) {
    let group = groups.get(choice.instanceId);
    if (!group) {
      group = { instanceId: choice.instanceId, instanceName: choice.instanceName || choice.instanceId, choices: [] };
      groups.set(choice.instanceId, group);
    }
    group.choices.push(choice);
  }
  return [...groups.values()];
}

@Component({
  selector: 'app-edit-symlink-dialog',
  imports: [FormField, MatButtonModule, MatDialogModule, MatInputModule, MatSelectModule],
  templateUrl: './edit-symlink-dialog.component.html',
  styleUrl: './edit-symlink-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EditSymlinkDialogComponent {
  private readonly service = inject(SymlinkService);
  private readonly destroy = inject(DestroyRef);
  private readonly i18n = inject(AppI18nService);
  private readonly dialog = inject(MatDialogRef<EditSymlinkDialogComponent>);
  protected readonly existing = inject<SymlinkLink | undefined>(MAT_DIALOG_DATA);
  protected readonly choices = signal<SymlinkChoices>({ movies: [], specials: [] });
  protected readonly model = signal({ movie: '', special: '' });
  protected readonly saving = signal(false);
  protected readonly fields = form(this.model, (schema) => {
    disabled(schema.movie, { when: () => this.saving() });
    disabled(schema.special, { when: () => this.saving() });
  });
  protected readonly searchModel = signal({ movie: '', special: '' });
  protected readonly searchFields = form(this.searchModel);
  protected readonly visibleMovies = computed(() =>
    this.choices().movies.filter(
      (value) =>
        `${value.instanceId}:${value.movieId}` === this.model().movie ||
        `${value.title} ${value.instanceName} ${value.path}`.toLocaleLowerCase().includes(this.searchModel().movie.toLocaleLowerCase()),
    ),
  );
  protected readonly visibleSpecials = computed(() =>
    this.choices().specials.filter(
      (value) =>
        `${value.instanceId}:${value.episodeId}` === this.model().special ||
        `${value.seriesTitle} ${value.title} ${value.instanceName}`
          .toLocaleLowerCase()
          .includes(this.searchModel().special.toLocaleLowerCase()),
    ),
  );
  protected readonly movieGroups = computed(() => groupByInstance(this.visibleMovies()));
  protected readonly specialGroups = computed(() => groupByInstance(this.visibleSpecials()));
  protected readonly plan = signal<SymlinkLinkPlan | null>(null);
  protected readonly error = signal('');
  protected readonly busy = signal(false);
  private configRevision = 0;
  private previewSelection = '';
  private previewVersion = 0;
  protected readonly previewLoading = signal(false);
  protected readonly selectedMovie = computed(() =>
    this.choices().movies.find((value) => `${value.instanceId}:${value.movieId}` === this.model().movie),
  );
  protected readonly selectedSpecial = computed(() =>
    this.choices().specials.find((value) => `${value.instanceId}:${value.episodeId}` === this.model().special),
  );

  constructor() {
    void this.load();
  }

  protected onSelectionChanged(): void {
    void this.preview();
  }

  protected t(key: TranslationKey, replace?: Record<string, string>): string {
    return this.i18n.t(key, replace);
  }

  protected clearSearch(field: 'movie' | 'special'): void {
    this.searchModel.update((value) => ({ ...value, [field]: '' }));
  }

  private async load(): Promise<void> {
    this.busy.set(true);
    try {
      const [choices] = await Promise.all([this.service.loadChoices(), this.service.loadSettings()]);
      this.choices.set(choices);
      this.configRevision = this.service.settings()!.revision;
      if (this.existing)
        this.model.set({
          movie: `${this.existing.radarrInstanceId}:${this.existing.radarrMovieId}`,
          special: `${this.existing.sonarrInstanceId}:${this.existing.sonarrEpisodeId}`,
        });
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : this.t('symlinks.loadError'));
    } finally {
      this.busy.set(false);
    }
    if (!this.destroy.destroyed && this.request()) await this.preview();
  }

  private request(): PreviewSymlinkLinkRequest | undefined {
    const movie = this.selectedMovie();
    const special = this.selectedSpecial();
    if (!movie || !special) return undefined;
    return {
      radarrInstanceId: movie.instanceId,
      radarrMovieId: movie.movieId,
      radarrFileId: movie.fileId,
      sonarrInstanceId: special.instanceId,
      sonarrSeriesId: special.seriesId,
      sonarrEpisodeId: special.episodeId,
      ...(this.existing ? { replacesId: this.existing.id } : {}),
    };
  }

  private async preview(keepError = false): Promise<void> {
    if (this.saving()) return;
    const version = ++this.previewVersion;
    const request = this.request();
    this.plan.set(null);
    this.previewSelection = '';
    if (!keepError) this.error.set('');
    if (!request) {
      this.previewLoading.set(false);
      this.busy.set(false);
      return;
    }
    const selection = JSON.stringify(request);
    const isCurrent = () => !this.destroy.destroyed && version === this.previewVersion && selection === JSON.stringify(this.request());
    this.busy.set(true);
    this.previewLoading.set(true);
    try {
      await this.service.loadSettings();
      if (!isCurrent()) return;
      const revision = this.service.settings()!.revision;
      const plan = await this.service.previewLink(request);
      if (isCurrent()) {
        this.configRevision = revision;
        this.previewSelection = selection;
        this.plan.set(plan);
      }
    } catch (error) {
      if (isCurrent()) this.error.set(error instanceof Error ? error.message : this.t('symlinks.preview.error'));
    } finally {
      if (version === this.previewVersion && !this.destroy.destroyed) {
        this.previewLoading.set(false);
        this.busy.set(false);
      }
    }
  }

  protected async save(): Promise<void> {
    const request = this.request();
    const plan = this.plan();
    if (!request || !plan?.runnable || this.busy()) return;
    if (JSON.stringify(request) !== this.previewSelection) {
      this.plan.set(null);
      return;
    }
    let failed = false;
    this.saving.set(true);
    this.busy.set(true);
    this.error.set('');
    try {
      const link = await this.service.saveLink(
        {
          ...request,
          configRevision: this.configRevision,
          revision: this.existing?.revision ?? 0,
          sourceVersion: plan.draft.sourceVersion,
          ...(!this.existing && plan.adoptsExisting && typeof plan.draft.sonarrFileId === 'number'
            ? { existingLink: { path: plan.draft.linkPath, linkText: plan.draft.linkText, sonarrFileId: plan.draft.sonarrFileId } }
            : {}),
        },
        this.existing?.id,
      );
      this.dialog.close(link);
    } catch (error) {
      failed = true;
      this.error.set(error instanceof Error ? error.message : this.t('symlinks.actionError'));
      this.plan.set(null);
    } finally {
      this.saving.set(false);
      this.busy.set(false);
    }
    if (failed && !this.destroy.destroyed) await this.preview(true);
  }
}
