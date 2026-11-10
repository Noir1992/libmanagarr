import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatInputModule } from '@angular/material/input';
import { SymlinkSuggestion } from '../../../interfaces/symlinks/symlink-suggestion';
import { AppI18nService } from '../../../services/app-i18n.service';
import { TranslationKey } from '../../../services/i18n/_i18n-key';

export interface RejectSymlinkDialogData {
  suggestion: SymlinkSuggestion;
}

/**
 * Asks for the reason behind a rejection and hands it to the caller. Only the pair is named here, never a
 * path — a rejection stays between the two application identifiers, which is what lets it survive the pair
 * moving to another folder. It is reversible through the rejection archive, so the detail is for whoever
 * reads the archive later.
 */
@Component({
  selector: 'app-reject-symlink-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatInputModule],
  templateUrl: './reject-symlink-dialog.component.html',
  styleUrl: './reject-symlink-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RejectSymlinkDialogComponent {
  private readonly i18n = inject(AppI18nService);
  private readonly dialogRef = inject(MatDialogRef<RejectSymlinkDialogComponent>);
  protected readonly data: RejectSymlinkDialogData = inject<RejectSymlinkDialogData>(MAT_DIALOG_DATA);
  protected readonly form = inject(NonNullableFormBuilder).group({ detail: [''] });

  protected t(key: TranslationKey, replace?: Record<string, string>): string {
    return this.i18n.t(key, replace);
  }

  protected episodeLabel(): string {
    return this.t('symlinks.specialNumber', {
      episode: String(this.data.suggestion.episodeNumber),
    });
  }

  protected reject(): void {
    this.dialogRef.close(this.form.controls.detail.value.trim());
  }

  protected cancel(): void {
    this.dialogRef.close();
  }
}
