import { ChangeDetectionStrategy, Component, effect, inject, OnDestroy, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatInputModule } from '@angular/material/input';
import { FeedbackState } from '../../../../interfaces/general/feedback-state';
import { FeedbackIconWidget } from '../../../../widgets/feedback-icon/feedback-icon.widget';
import { InfoIconWidget } from '../../../../widgets/info-icon/info-icon.widget';
import { AppI18nService } from '../../../../services/app-i18n.service';
import { AppSettingsService, ValidationProblemError } from '../../../../services/app-settings.service';
import { TranslationKey } from '../../../../services/i18n/_i18n-key';
import { isTitleWhitespace, normalizeLeadingWords, trimTitleWhitespace } from '../../../../utils/title-sort-key-utils';

@Component({
  selector: 'app-title-sorting-settings-card',
  imports: [ReactiveFormsModule, MatInputModule, FeedbackIconWidget, InfoIconWidget],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './title-sorting-settings-card.widget.html',
  styleUrl: './title-sorting-settings-card.widget.scss',
})
export class TitleSortingSettingsCardWidget implements OnDestroy {
  private readonly settings = inject(AppSettingsService);
  private readonly i18n = inject(AppI18nService);
  protected readonly form = new FormGroup({
    ignoredLeadingWords: new FormControl('', { nonNullable: true }),
  });
  protected readonly saving = signal(false);
  protected readonly wordError = signal<string | null>(null);
  protected readonly feedback = signal<string | null>(null);
  protected readonly feedbackState = signal<FeedbackState>(null);
  private feedbackTimeout?: ReturnType<typeof setTimeout>;
  private readonly syncSettings = effect(() => {
    this.settings.titleSorting();
    if (!this.hasUnsavedChanges()) this.resetForm();
  });

  protected t(key: TranslationKey): string {
    return this.i18n.t(key);
  }
  ngOnDestroy(): void {
    clearTimeout(this.feedbackTimeout);
  }
  isBusy(): boolean {
    return this.saving();
  }
  hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }
  collectUnsavedChangeDescriptions(): string[] {
    return this.hasUnsavedChanges() ? [this.t('settings.titleSorting.title')] : [];
  }
  discardUnsavedChanges(): void {
    this.resetForm();
    this.wordError.set(null);
    this.feedback.set(null);
    this.feedbackState.set(null);
  }

  protected onWordsInput(): void {
    this.wordError.set(null);
    this.feedback.set(null);
    this.feedbackState.set(null);
  }

  protected async save(): Promise<void> {
    if (this.saving()) return;
    const words = this.form.controls.ignoredLeadingWords.value
      .split(',')
      .map((word) => trimTitleWhitespace(word).normalize('NFC'))
      .filter((word) => word.length > 0);
    if (words.some((word) => Array.from(word).length > 64 || /[\p{Cc}\p{Cs}]/u.test(word) || Array.from(word).some(isTitleWhitespace))) {
      this.wordError.set(this.t('settings.titleSorting.invalidWord'));
      this.feedbackState.set('error');
      return;
    }
    const normalizedWords = normalizeLeadingWords(words);
    if (normalizedWords.length > 100) {
      this.wordError.set(this.t('settings.titleSorting.tooManyWords'));
      this.feedbackState.set('error');
      return;
    }
    const enabled = normalizedWords.length > 0;
    const saved = this.settings.titleSorting();
    if (saved.enabled === enabled && JSON.stringify(saved.ignoredLeadingWords) === JSON.stringify(normalizedWords)) {
      this.resetForm();
      return;
    }
    clearTimeout(this.feedbackTimeout);
    this.saving.set(true);
    this.form.disable({ emitEvent: false });
    this.feedback.set(null);
    this.wordError.set(null);
    try {
      await this.settings.saveTitleSorting({
        enabled,
        ignoredLeadingWords: normalizedWords,
      });
      this.resetForm();
      this.feedback.set(this.t('settings.titleSorting.saved'));
      this.feedbackState.set('success');
      this.feedbackTimeout = setTimeout(() => this.feedbackState.set(null), 2200);
    } catch (error) {
      if (error instanceof ValidationProblemError) {
        for (const [field, messages] of Object.entries(error.fieldErrors)) {
          if (field.toLowerCase().includes('ignoredleadingwords')) this.wordError.set(messages[0]);
        }
      }
      this.feedback.set(error instanceof Error ? error.message : this.t('settings.saveError'));
      this.feedbackState.set('error');
    } finally {
      this.form.enable({ emitEvent: false });
      this.saving.set(false);
    }
  }
  private resetForm(): void {
    const saved = this.settings.titleSorting();
    this.form.reset({ ignoredLeadingWords: saved.ignoredLeadingWords.join(', ') }, { emitEvent: false });
  }
}
