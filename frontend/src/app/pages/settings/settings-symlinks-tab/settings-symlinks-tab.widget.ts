import { ChangeDetectionStrategy, Component, computed, effect, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatInputModule } from '@angular/material/input';
import { firstValueFrom, Subscription } from 'rxjs';
import { MatDialog } from '@angular/material/dialog';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatIconModule } from '@angular/material/icon';
import { DirectoryBrowserDialogComponent } from './directory-browser-dialog/directory-browser-dialog.component';
import { FeedbackState } from '../../../interfaces/general/feedback-state';
import { SymlinkService } from '../../../services/symlink.service';
import { TranslationKey } from '../../../services/i18n/_i18n-key';
import { FeedbackIconWidget } from '../../../widgets/feedback-icon/feedback-icon.widget';
import { SettingsUnsavedChangesBase } from '../settings-unsaved-changes.base';
import { controlPathFromValidationKey } from './settings-symlinks-tab.util';

@Component({
  selector: 'app-settings-symlinks-tab-widget',
  imports: [ReactiveFormsModule, MatSlideToggleModule, MatIconModule, MatButtonModule, MatInputModule, FeedbackIconWidget],
  templateUrl: './settings-symlinks-tab.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './settings-symlinks-tab.widget.scss',
})
export class SettingsSymlinksTabWidget extends SettingsUnsavedChangesBase implements OnInit, OnDestroy {
  protected readonly symlinkService = inject(SymlinkService);
  private readonly dialog = inject(MatDialog);
  protected readonly form = new FormGroup({
    enabled: new FormControl(false, { nonNullable: true }),
    mediaRoot: new FormControl('/media', { nonNullable: true }),
  });
  protected readonly feedback = signal<string | null>(null);
  protected readonly feedbackState = signal<FeedbackState>(null);
  protected readonly generalError = computed(() => {
    const hasMediaRootError = Object.keys(this.symlinkService.fieldErrors()).some(
      (key) => controlPathFromValidationKey(key) === 'mediaRoot',
    );
    return hasMediaRootError ? null : this.symlinkService.settingsError();
  });
  protected readonly capabilityChecks = computed(() => this.symlinkService.settings()?.capabilityChecks ?? []);
  protected readonly canEnable = computed(() => {
    const checks = this.capabilityChecks();
    return (
      !this.symlinkService.settingsLoading() &&
      !this.symlinkService.capabilityChecking() &&
      !this.symlinkService.capabilityError() &&
      checks.length > 0 &&
      checks.every((check) => check.status === 'Available' && !check.stale)
    );
  });
  protected readonly displayedEnabled = computed(
    () =>
      (this.symlinkService.settings()?.enabled ?? this.canEnable()) &&
      !this.capabilityChecks().some((check) => check.status === 'Unsupported' && !check.stale),
  );
  private readonly retryCapabilityCheck = effect((onCleanup) => {
    if (
      !this.symlinkService.settings()?.enabled ||
      this.canEnable() ||
      this.symlinkService.settingsLoading() ||
      this.symlinkService.capabilityChecking() ||
      this.capabilityChecks().some((check) => check.status === 'Unsupported' && !check.stale)
    )
      return;
    const timer = setTimeout(() => {
      void this.symlinkService.checkCapabilities().catch(() => undefined);
    }, 10000);
    onCleanup(() => clearTimeout(timer));
  });
  protected readonly enableHint = computed(() => {
    if (this.symlinkService.settingsLoading() || this.symlinkService.capabilityChecking())
      return this.t('settings.symlinks.capability.checking');
    if (this.canEnable()) return null;
    return (
      this.symlinkService.capabilityError() ??
      this.capabilityChecks().find((check) => check.status !== 'Available' && !check.stale)?.detail ??
      this.t('settings.symlinks.capability.required')
    );
  });
  private readonly subscriptions = new Subscription();
  private readonly autoSaving = signal(false);
  protected readonly feedbackField = signal<'enabled' | 'mediaRoot'>('mediaRoot');
  private pendingSave: Promise<void> | null = null;
  private feedbackTimeout?: ReturnType<typeof setTimeout>;
  private readonly syncSettings = effect(() => {
    this.symlinkService.settings();
    if (!this.hasUnsavedChanges()) this.resetForm();
  });

  private readonly syncEnabledControl = effect(() => {
    const allowed = this.canEnable();
    const busy = this.autoSaving();
    const control = this.form.controls.enabled;
    if (!allowed) control.setValue(this.displayedEnabled(), { emitEvent: false });
    if (!allowed || busy) control.disable({ emitEvent: false });
    else {
      if (!control.dirty) control.setValue(this.displayedEnabled(), { emitEvent: false });
      control.enable({ emitEvent: false });
    }
  });

  protected t(key: TranslationKey): string {
    return this.i18n.t(key);
  }

  async ngOnInit(): Promise<void> {
    this.subscriptions.add(
      this.form.valueChanges.subscribe(() => {
        clearTimeout(this.feedbackTimeout);
        this.feedback.set(null);
        this.feedbackState.set(null);
        this.form.controls.mediaRoot.setErrors(null);
      }),
    );
    try {
      await this.symlinkService.loadSettings();
      await this.symlinkService.checkCapabilities();
    } catch {
      /* Settings and automatic-check errors are rendered beside the controls. */
    }
    if (!this.hasUnsavedChanges()) this.resetForm();
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
    clearTimeout(this.feedbackTimeout);
  }
  protected override isBusy(): boolean {
    return this.autoSaving() || this.symlinkService.saving();
  }
  protected override hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }
  protected override collectUnsavedChangeDescriptions(): string[] {
    return [this.t('settings.symlinks.title')];
  }
  protected override discardUnsavedChanges(): void {
    clearTimeout(this.feedbackTimeout);
    this.resetForm();
    this.feedback.set(null);
    this.feedbackState.set(null);
  }

  protected saveAsync(field: 'enabled' | 'mediaRoot' = 'mediaRoot'): Promise<void> {
    if (this.pendingSave) return this.pendingSave;
    const saving = this.persistAsync(field);
    this.pendingSave = saving;
    void saving.finally(() => {
      if (this.pendingSave === saving) this.pendingSave = null;
    });
    return saving;
  }

  protected async browseDirectory(): Promise<void> {
    const path = (await firstValueFrom(
      this.dialog
        .open(DirectoryBrowserDialogComponent, {
          width: '640px',
          maxWidth: '95vw',
          maxHeight: 'calc(100dvh - 48px)',
          data: { path: this.form.controls.mediaRoot.value },
        })
        .afterClosed(),
    )) as string | undefined;
    if (typeof path !== 'string' || path.trim().length === 0) return;
    await this.pendingSave;
    this.form.controls.mediaRoot.setValue(path);
    this.form.markAsDirty();
    await this.saveAsync();
  }

  protected structureExample(): string {
    const root = this.form.controls.mediaRoot.value.trim().replace(/[\\/]+$/, '') || '/media';
    return `${root}/
├── movies/
│   └── Example Movie/
│       └── movie.mkv
└── series/
    └── Example Series/
        └── Specials/
            └── Special 01.mkv → ../../../movies/Example Movie/movie.mkv`;
  }

  private async persistAsync(field: 'enabled' | 'mediaRoot'): Promise<void> {
    const saved = this.symlinkService.settings();
    if (this.isBusy() || saved === null) return;
    const mediaRoot = this.form.controls.mediaRoot.value;
    const enabled = field === 'enabled' ? this.form.controls.enabled.value : (saved.enabled ?? this.form.controls.enabled.value);
    if (mediaRoot.trim() === saved.mediaRoot && saved.pathMappings.length === 0 && enabled === (saved.enabled ?? true)) {
      this.resetForm();
      return;
    }
    clearTimeout(this.feedbackTimeout);
    this.feedback.set(null);
    this.feedbackState.set(null);
    this.feedbackField.set(field);
    this.autoSaving.set(true);
    this.form.disable({ emitEvent: false });
    try {
      await this.symlinkService.saveSettings({ mediaRoot, enabled });
      this.resetForm();
      this.feedback.set(this.t('settings.symlinks.saved'));
      this.feedbackState.set('success');
      this.feedbackTimeout = setTimeout(() => this.feedbackState.set(null), 2200);
    } catch {
      // Enabling recalculates validity, so apply server errors afterwards.
      this.form.enable({ emitEvent: false });
      for (const [key, message] of Object.entries(this.symlinkService.fieldErrors())) {
        const path = controlPathFromValidationKey(key);
        if (path === 'mediaRoot') this.form.controls.mediaRoot.setErrors({ server: message });
      }
      this.feedback.set(this.symlinkService.settingsError());
      this.feedbackState.set('error');
    } finally {
      if (this.form.disabled) this.form.enable({ emitEvent: false });
      this.autoSaving.set(false);
      if (!this.canEnable()) {
        this.form.controls.enabled.setValue(this.displayedEnabled(), { emitEvent: false });
        this.form.controls.enabled.disable({ emitEvent: false });
      }
    }
  }

  protected fieldError(): string | null {
    const error: unknown = this.form.controls.mediaRoot.errors?.['server'];
    return typeof error === 'string' ? error : null;
  }

  private resetForm(): void {
    this.form.reset(
      {
        enabled: this.displayedEnabled(),
        mediaRoot: this.symlinkService.settings()?.mediaRoot ?? '/media',
      },
      { emitEvent: false },
    );
  }
}
