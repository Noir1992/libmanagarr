import { ChangeDetectionStrategy, Component, computed, inject, Signal } from '@angular/core';
import { MatButtonModule, MatIconButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { AppI18nService } from '../../services/app-i18n.service';

export interface RequestDeleteDialogData {
  readonly title: string | null;
}

@Component({
  selector: 'app-request-delete-dialog',
  imports: [MatButtonModule, MatDialogModule, MatIconButton],
  templateUrl: './request-delete-dialog.widget.html',
  styleUrl: './request-delete-dialog.widget.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RequestDeleteDialogWidget {
  protected readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly dialogRef: MatDialogRef<RequestDeleteDialogWidget> = inject(MatDialogRef<RequestDeleteDialogWidget>);
  readonly data: RequestDeleteDialogData = inject(MAT_DIALOG_DATA);

  protected readonly titleLabel: Signal<string> = computed((): string => this.i18n.t('requests.delete.title'));
  protected readonly confirmText: Signal<string> = computed((): string => this.i18n.t('requests.delete.confirm'));
  protected readonly confirmLabel: Signal<string> = computed((): string => this.i18n.t('requests.action.delete'));
  protected readonly cancelLabel: Signal<string> = computed((): string => this.i18n.t('common.cancel'));
  protected readonly closeLabel: Signal<string> = computed((): string => this.i18n.t('common.close'));

  protected cancel(): void {
    this.dialogRef.close();
  }

  protected confirm(): void {
    this.dialogRef.close(true);
  }
}
