import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { ExistingLinkConfirmation, SymlinkFinding, SymlinkLink, SymlinkLinkPlan } from '../../../interfaces/symlinks/symlink-link';
import { SymlinkSuggestion } from '../../../interfaces/symlinks/symlink-suggestion';
import { AppI18nService } from '../../../services/app-i18n.service';
import { SymlinkService } from '../../../services/symlink.service';
import { TranslationKey } from '../../../services/i18n/_i18n-key';

/** The pair to lay out plus the path configuration the preview below belongs to. */
export interface ConfirmSymlinkDialogData {
  suggestion: SymlinkSuggestion;
  configRevision: number;
}

/**
 * Shows what confirming would write before anything is written. The preview is read when the dialog opens,
 * so the plan here is the one the path configuration of this moment produces — and the confirm carries that
 * configuration's revision, which is what refuses the write if the paths moved while this window was open.
 */
@Component({
  selector: 'app-confirm-symlink-dialog',
  imports: [MatDialogModule, MatButtonModule, MatIconModule],
  templateUrl: './confirm-symlink-dialog.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './confirm-symlink-dialog.component.scss',
})
export class ConfirmSymlinkDialogComponent {
  private readonly i18n = inject(AppI18nService);
  private readonly dialogRef = inject(MatDialogRef<ConfirmSymlinkDialogComponent>);
  private readonly symlinks = inject(SymlinkService);
  protected readonly data: ConfirmSymlinkDialogData = inject<ConfirmSymlinkDialogData>(MAT_DIALOG_DATA);

  protected readonly plan = signal<SymlinkLinkPlan | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected t(key: TranslationKey, replace?: Record<string, string>): string {
    return this.i18n.t(key, replace);
  }

  constructor() {
    void this.loadPreview();
  }

  protected episodeLabel(): string {
    return this.t('symlinks.episodeNumber', {
      season: String(this.data.suggestion.seasonNumber),
      episode: String(this.data.suggestion.episodeNumber),
    });
  }

  protected findings(plan: SymlinkLinkPlan): SymlinkFinding[] {
    return [...plan.blockers, ...plan.warnings];
  }

  protected async confirmAsync(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      const proposed = this.plan();
      const consent: ExistingLinkConfirmation | undefined =
        proposed?.adoptsExisting && typeof proposed.draft.sonarrFileId === 'number'
          ? { path: proposed.draft.linkPath, linkText: proposed.draft.linkText, sonarrFileId: proposed.draft.sonarrFileId }
          : undefined;
      const link: SymlinkLink = consent
        ? await this.symlinks.confirmSuggestion(this.data.suggestion.id, this.data.configRevision, proposed?.draft.sourceVersion, consent)
        : await this.symlinks.confirmSuggestion(this.data.suggestion.id, this.data.configRevision, proposed?.draft.sourceVersion);
      this.dialogRef.close(link);
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : this.t('symlinks.confirm.refused'));
      this.busy.set(false);
    }
  }

  protected close(): void {
    this.dialogRef.close();
  }

  private async loadPreview(): Promise<void> {
    const { radarrInstanceId, radarrMovieId, sonarrInstanceId, sonarrSeriesId, sonarrEpisodeId, radarrFileId } = this.data.suggestion;
    try {
      this.plan.set(
        await this.symlinks.previewLink({
          radarrInstanceId,
          radarrMovieId,
          sonarrInstanceId,
          sonarrSeriesId,
          sonarrEpisodeId,
          radarrFileId,
        }),
      );
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : this.t('symlinks.preview.error'));
    } finally {
      this.loading.set(false);
    }
  }
}
