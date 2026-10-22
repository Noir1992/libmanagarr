import { inject } from '@angular/core';
import { CanDeactivateWithPendingChanges } from '../../interfaces/general/can-deactivate-with-pending-changes';
import { AppI18nService } from '../../services/app-i18n.service';

export abstract class SettingsUnsavedChangesBase implements CanDeactivateWithPendingChanges {
  protected readonly i18n: AppI18nService = inject(AppI18nService);

  canDeactivate(): boolean {
    if (this.isBusy()) {
      return false;
    }

    if (!this.hasUnsavedChanges()) {
      return true;
    }

    if (!globalThis.confirm(this.buildUnsavedChangesMessage())) {
      return false;
    }

    this.discardUnsavedChanges();
    return true;
  }

  protected buildUnsavedChangesMessage(): string {
    const lines: string[] = [this.i18n.t('settings.unsavedChangesTitle'), this.i18n.t('settings.unsavedChangesMessage')];
    const changedFields: string[] = this.collectUnsavedChangeDescriptions()
      .map((value) => value.trim())
      .filter((value) => value.length > 0);

    if (changedFields.length > 0) {
      lines.push('', this.i18n.t('settings.unsavedChangesFields'), ...changedFields.map((value) => `- ${value}`));
    }

    return lines.join('\n');
  }

  protected collectUnsavedChangeDescriptions(): string[] {
    return [];
  }

  protected abstract isBusy(): boolean;

  protected abstract hasUnsavedChanges(): boolean;

  protected abstract discardUnsavedChanges(): void;
}
