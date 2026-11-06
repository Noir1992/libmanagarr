import { ChangeDetectionStrategy, Component, computed, effect, EffectRef, inject, OnDestroy, OnInit, signal, Signal } from '@angular/core';
import { FormArray, FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Subscription } from 'rxjs';
import { ARR_APPLICATION_KINDS, ArrApplicationKind } from '../../../interfaces/general/arr-application-kind';
import { PathReaderKind } from '../../../enums/path-reader-kind';
import { PathReaderVerification } from '../../../enums/path-reader-verification';
import { FeedbackState } from '../../../interfaces/general/feedback-state';
import { ArrInstanceSettings } from '../../../interfaces/settings/arr-instance-settings';
import { ArrPathMapping, MediaStorageArea, PathReaderView, SymlinkCapabilityCheck, SymlinkSettings } from '../../../interfaces/symlinks/symlink-settings';
import { AppDatePipe } from '../../../pipes/app-date.pipe';
import { AppSettingsService } from '../../../services/app-settings.service';
import { SymlinkService } from '../../../services/symlink.service';
import { TranslationKey } from '../../../services/i18n/_i18n-key';
import { FeedbackIconWidget } from '../../../widgets/feedback-icon/feedback-icon.widget';
import { InfoIconWidget } from '../../../widgets/info-icon/info-icon.widget';
import { SettingsUnsavedChangesBase } from '../settings-unsaved-changes.base';
import {
  capabilityFeedbackState,
  capabilityStatusLabel,
  controlPathFromValidationKey,
  readerKindLabel,
  verificationLabel,
} from './settings-symlinks-tab.util';

/**
 * The row identity stays with the row, so a probe answer cannot drift to another shelf when a row is
 * inserted above it. New rows carry an empty id, which is what makes the server give them one.
 */
type StorageAreaGroup = FormGroup<{
  id: FormControl<string>;
  name: FormControl<string>;
  moviesRoot: FormControl<string>;
  seriesRoot: FormControl<string>;
}>;

type PathMappingGroup = FormGroup<{
  id: FormControl<string>;
  kind: FormControl<ArrApplicationKind>;
  instanceId: FormControl<string>;
  instanceRoot: FormControl<string>;
  libManagarrRoot: FormControl<string>;
}>;

/**
 * The verification state is what a real read proved, so it travels with the row unbound: the template shows
 * its label in a disabled field, and the raw value goes back out because the server keeps what it is sent.
 */
type ReaderViewGroup = FormGroup<{
  id: FormControl<string>;
  kind: FormControl<PathReaderKind>;
  root: FormControl<string>;
  verification: FormControl<PathReaderVerification>;
}>;

@Component({
  selector: 'app-settings-symlinks-tab-widget',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    FeedbackIconWidget,
    InfoIconWidget,
    AppDatePipe,
  ],
  templateUrl: './settings-symlinks-tab.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './settings-symlinks-tab.widget.scss',
})
export class SettingsSymlinksTabWidget extends SettingsUnsavedChangesBase implements OnInit, OnDestroy {
  private readonly appSettings: AppSettingsService = inject(AppSettingsService);
  protected readonly symlinkService: SymlinkService = inject(SymlinkService);

  protected readonly form = new FormGroup({
    storageAreas: new FormArray<StorageAreaGroup>([]),
    pathMappings: new FormArray<PathMappingGroup>([]),
    readerViews: new FormArray<ReaderViewGroup>([]),
  });

  protected readonly feedback = signal<string | null>(null);
  protected readonly feedbackState = signal<FeedbackState>(null);

  protected readonly mappingKindOptions: readonly ArrApplicationKind[] = [ARR_APPLICATION_KINDS.SONARR, ARR_APPLICATION_KINDS.RADARR];
  protected readonly readerKindOptions: readonly PathReaderKind[] = ['Sonarr', 'MediaPlayer'];

  protected readonly capabilityChecks: Signal<SymlinkCapabilityCheck[]> = computed(
    () => this.symlinkService.settings()?.capabilityChecks ?? [],
  );
  protected readonly savedAreaNames: Signal<Record<string, string>> = computed(() => {
    const names: Record<string, string> = {};
    for (const area of this.symlinkService.settings()?.storageAreas ?? []) names[area.id] = area.name;
    return names;
  });

  private readonly subscriptions: Subscription = new Subscription();
  /** Rebuilding the rows is not an edit, so it must not retire the sentence the save just produced. */
  private rebuilding = false;
  private readonly syncSettings: EffectRef = effect(() => {
    this.symlinkService.settings();
    if (!this.hasUnsavedChanges()) this.resetForm();
  });

  protected t(key: TranslationKey): string {
    return this.i18n.t(key);
  }

  async ngOnInit(): Promise<void> {
    // An edit retires the previous answer; the sentence belongs to the attempt that produced it.
    this.subscriptions.add(
      this.form.valueChanges.subscribe(() => {
        if (this.rebuilding) return;
        this.clearFeedback();
      }),
    );
    try {
      await this.symlinkService.loadSettings();
    } catch {
      /* The load error is rendered above the form. */
    }
    try {
      // A mapping names its instance by id, so the choices are the ones the server validates against.
      await this.appSettings.loadInstances();
    } catch {
      /* An instance that cannot be listed is refused by the save with the reason beside its field. */
    }
    this.resetForm();
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }

  protected get storageAreas(): FormArray<StorageAreaGroup> {
    return this.form.controls.storageAreas;
  }

  protected get pathMappings(): FormArray<PathMappingGroup> {
    return this.form.controls.pathMappings;
  }

  protected get readerViews(): FormArray<ReaderViewGroup> {
    return this.form.controls.readerViews;
  }

  protected override isBusy(): boolean {
    return this.symlinkService.saving();
  }

  protected override hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }

  protected override collectUnsavedChangeDescriptions(): string[] {
    return this.hasUnsavedChanges() ? [this.t('settings.symlinks.title')] : [];
  }

  protected override discardUnsavedChanges(): void {
    this.resetForm();
    this.clearFeedback();
  }

  protected addStorageArea(): void {
    this.storageAreas.push(
      new FormGroup({
        id: new FormControl('', { nonNullable: true }),
        name: new FormControl('', { nonNullable: true }),
        moviesRoot: new FormControl('', { nonNullable: true }),
        seriesRoot: new FormControl('', { nonNullable: true }),
      }) as StorageAreaGroup,
    );
    this.form.markAsDirty();
  }

  protected addPathMapping(): void {
    this.pathMappings.push(
      new FormGroup({
        id: new FormControl('', { nonNullable: true }),
        kind: new FormControl<ArrApplicationKind>(ARR_APPLICATION_KINDS.RADARR, { nonNullable: true }),
        instanceId: new FormControl('', { nonNullable: true }),
        instanceRoot: new FormControl('', { nonNullable: true }),
        libManagarrRoot: new FormControl('', { nonNullable: true }),
      }) as PathMappingGroup,
    );
    this.form.markAsDirty();
  }

  /** One view per reader is all the rules allow, so the next free kind is the one worth offering. */
  protected addReaderView(): void {
    const taken = this.readerViews.controls.map((view) => view.controls.kind.value);
    const kind: PathReaderKind = taken.includes('Sonarr') ? 'MediaPlayer' : 'Sonarr';
    this.readerViews.push(
      new FormGroup({
        id: new FormControl('', { nonNullable: true }),
        kind: new FormControl<PathReaderKind>(kind, { nonNullable: true }),
        root: new FormControl('', { nonNullable: true }),
        verification: new FormControl<PathReaderVerification>('Unverified', { nonNullable: true }),
      }) as ReaderViewGroup,
    );
    this.form.markAsDirty();
  }

  protected removeStorageArea(index: number): void {
    this.storageAreas.removeAt(index);
    this.form.markAsDirty();
    this.clearFeedback();
  }

  protected removePathMapping(index: number): void {
    this.pathMappings.removeAt(index);
    this.form.markAsDirty();
    this.clearFeedback();
  }

  protected removeReaderView(index: number): void {
    this.readerViews.removeAt(index);
    this.form.markAsDirty();
    this.clearFeedback();
  }

  /** The instance list belongs to one kind, so a kind that moved invalidates the instance chosen under it. */
  protected handleMappingKindChanged(group: PathMappingGroup): void {
    group.controls.instanceId.setValue('');
    this.form.markAsDirty();
  }

  /**
   * A verification states what a real read proved about one process and one root, and the server keeps
   * whatever it is sent. So the row that edited either of them is unverified again — otherwise a saved
   * 'Verified' would go on certifying links under a root nobody ever read through.
   */
  protected resetVerification(group: ReaderViewGroup): void {
    if (group.controls.verification.value === 'Unverified') return;
    group.controls.verification.setValue('Unverified');
    this.form.markAsDirty();
  }

  protected instancesFor(kind: ArrApplicationKind): ArrInstanceSettings[] {
    return kind === ARR_APPLICATION_KINDS.RADARR ? this.appSettings.radarrInstances() : this.appSettings.sonarrInstances();
  }

  protected capabilityLabel(check: SymlinkCapabilityCheck): string {
    return capabilityStatusLabel(check.status, this.i18n.t.bind(this.i18n));
  }

  protected capabilityFeedback(check: SymlinkCapabilityCheck): FeedbackState {
    const state = this.symlinkService.capabilityStates()[check.storageAreaId];
    if (state?.busy) return 'loading';
    if (state?.error) return 'error';
    return capabilityFeedbackState(check.status, check.stale);
  }

  /** The probe's own sentence wins; a failed probe is reported by the reason the answer carries. */
  protected capabilityMessage(check: SymlinkCapabilityCheck): string {
    return this.symlinkService.capabilityStates()[check.storageAreaId]?.error ?? check.detail ?? '';
  }

  protected async testCapabilityAsync(check: SymlinkCapabilityCheck): Promise<void> {
    try {
      await this.symlinkService.testCapability(check.storageAreaId);
    } catch {
      /* The answer of the failed probe is rendered beside its storage area. */
    }
  }

  protected fieldError(path: string): string | null {
    const error = this.form.get(path)?.errors?.['server'];
    return typeof error === 'string' ? error : null;
  }

  protected async saveAsync(): Promise<void> {
    if (this.isBusy()) return;
    this.clearFeedback();
    try {
      await this.symlinkService.saveSettings(this.draft());
      this.resetForm();
      this.feedback.set(this.t('settings.symlinks.saved'));
      this.feedbackState.set('success');
    } catch {
      this.applyServerErrors();
      this.feedback.set(this.symlinkService.settingsError());
      this.feedbackState.set('error');
    }
  }

  protected discardAsync(): Promise<void> {
    this.discardUnsavedChanges();
    return Promise.resolve();
  }

  protected readerKindLabel(kind: PathReaderKind): string {
    return readerKindLabel(kind, this.i18n.t.bind(this.i18n));
  }

  protected verificationLabel(verification: PathReaderVerification): string {
    return verificationLabel(verification, this.i18n.t.bind(this.i18n));
  }

  private draft(): SymlinkSettings {
    const saved = this.symlinkService.settings();
    const areas: MediaStorageArea[] = this.storageAreas.controls.map((group) => ({
      id: group.controls.id.value,
      name: group.controls.name.value,
      moviesRoot: group.controls.moviesRoot.value,
      seriesRoot: group.controls.seriesRoot.value,
    }));
    const mappings: ArrPathMapping[] = this.pathMappings.controls.map((group) => ({
      id: group.controls.id.value,
      kind: group.controls.kind.value,
      instanceId: group.controls.instanceId.value,
      instanceRoot: group.controls.instanceRoot.value,
      libManagarrRoot: group.controls.libManagarrRoot.value,
    }));
    // The verification state has no field of its own to type into; it goes back out as the row carries it.
    const views: PathReaderView[] = this.readerViews.controls.map((group) => ({
      id: group.controls.id.value,
      kind: group.controls.kind.value,
      root: group.controls.root.value,
      verification: group.controls.verification.value,
    }));
    return {
      storageAreas: areas,
      pathMappings: mappings,
      readerViews: views,
      revision: saved?.revision ?? 0,
      capabilityChecks: saved?.capabilityChecks ?? [],
    };
  }

  /** The lists are as long as the saved rows, because a row is a shelf or a rule, not a blank to fill. */
  private resetForm(): void {
    // Replacing the controls reads as a value change, and that answer belongs to the attempt before this one.
    this.rebuilding = true;
    const saved = this.symlinkService.settings();
    this.form.setControl(
      'storageAreas',
      new FormArray<StorageAreaGroup>(
        (saved?.storageAreas ?? []).map(
          (area) =>
            new FormGroup({
              id: new FormControl(area.id, { nonNullable: true }),
              name: new FormControl(area.name, { nonNullable: true }),
              moviesRoot: new FormControl(area.moviesRoot, { nonNullable: true }),
              seriesRoot: new FormControl(area.seriesRoot, { nonNullable: true }),
            }) as StorageAreaGroup,
        ),
      ),
    );
    this.form.setControl(
      'pathMappings',
      new FormArray<PathMappingGroup>(
        (saved?.pathMappings ?? []).map(
          (mapping) =>
            new FormGroup({
              id: new FormControl(mapping.id, { nonNullable: true }),
              kind: new FormControl<ArrApplicationKind>(mapping.kind, { nonNullable: true }),
              instanceId: new FormControl(mapping.instanceId, { nonNullable: true }),
              instanceRoot: new FormControl(mapping.instanceRoot, { nonNullable: true }),
              libManagarrRoot: new FormControl(mapping.libManagarrRoot, { nonNullable: true }),
            }) as PathMappingGroup,
        ),
      ),
    );
    this.form.setControl(
      'readerViews',
      new FormArray<ReaderViewGroup>(
        (saved?.readerViews ?? []).map(
          (view) =>
            new FormGroup({
              id: new FormControl(view.id, { nonNullable: true }),
              kind: new FormControl<PathReaderKind>(view.kind, { nonNullable: true }),
              root: new FormControl(view.root, { nonNullable: true }),
              verification: new FormControl<PathReaderVerification>(view.verification, { nonNullable: true }),
            }) as ReaderViewGroup,
        ),
      ),
    );
    this.form.markAsPristine();
    this.clearServerErrors();
    this.rebuilding = false;
  }

  private applyServerErrors(): void {
    for (const [key, message] of Object.entries(this.symlinkService.fieldErrors())) {
      const path = controlPathFromValidationKey(key);
      const control = path === null ? null : this.form.get(path);
      if (control) control.setErrors({ server: message });
    }
  }

  private clearServerErrors(): void {
    for (const group of [...this.storageAreas.controls, ...this.pathMappings.controls, ...this.readerViews.controls]) {
      for (const control of Object.values(group.controls)) {
        if (control.hasError('server')) control.setErrors(null);
      }
    }
  }

  private clearFeedback(): void {
    this.feedback.set(null);
    this.feedbackState.set(null);
  }
}
