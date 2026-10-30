import { ChangeDetectionStrategy, Component, computed, inject, Signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatInputModule } from '@angular/material/input';
import { AppI18nService } from '../../services/app-i18n.service';

export interface RequestDeclineDialogData {
  readonly title: string | null;
}

@Component({
  selector: 'app-request-decline-dialog',
  imports: [ReactiveFormsModule, MatButtonModule, MatDialogModule, MatInputModule],
  templateUrl: './request-decline-dialog.widget.html',
  styleUrl: './request-decline-dialog.widget.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RequestDeclineDialogWidget {
  protected readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly dialogRef: MatDialogRef<RequestDeclineDialogWidget> = inject(MatDialogRef<RequestDeclineDialogWidget>);
  readonly data: RequestDeclineDialogData = inject(MAT_DIALOG_DATA);
  protected readonly form = inject(NonNullableFormBuilder).group({ reason: [''] });

  protected readonly titleLabel: Signal<string> = computed((): string => this.i18n.t('requests.decline.title'));
  protected readonly reasonLabel: Signal<string> = computed((): string => this.i18n.t('requests.decline.reason'));
  protected readonly confirmLabel: Signal<string> = computed((): string => this.i18n.t('requests.action.decline'));
  protected readonly cancelLabel: Signal<string> = computed((): string => this.i18n.t('common.cancel'));

  protected cancel(): void {
    this.dialogRef.close();
  }

  protected confirm(): void {
    this.dialogRef.close(this.form.controls.reason.value.trim());
  }
}
