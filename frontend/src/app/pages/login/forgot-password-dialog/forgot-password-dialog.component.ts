import { ChangeDetectionStrategy, Component, computed, inject, Signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { AppI18nService } from '../../../services/app-i18n.service';

@Component({
  selector: 'app-forgot-password-dialog',
  imports: [MatDialogModule, MatButtonModule],
  templateUrl: './forgot-password-dialog.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './forgot-password-dialog.component.scss',
})
export class ForgotPasswordDialogComponent {
  private readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly dialogRef: MatDialogRef<ForgotPasswordDialogComponent> = inject(MatDialogRef);

  protected readonly title: Signal<string> = computed((): string => this.i18n.t('auth.forgotPasswordTitle'));
  protected readonly message: Signal<string> = computed((): string => this.i18n.t('auth.forgotPasswordMessage'));
  protected readonly closeLabel: Signal<string> = computed((): string => this.i18n.t('common.close'));

  protected close(): void {
    this.dialogRef.close();
  }
}
