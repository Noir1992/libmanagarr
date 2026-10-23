import { NgOptimizedImage } from '@angular/common';
import { Component, computed, inject, OnInit, Signal, signal, WritableSignal, ChangeDetectionStrategy } from '@angular/core';
import { FormArray, NonNullableFormBuilder, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { ARR_APPLICATION_KINDS, ArrApplicationKind } from '../../../interfaces/general/arr-application-kind';
import { ArrInstanceFormGroup } from '../../../interfaces/settings/arr-instance-form-group';
import { ArrInstanceSettings } from '../../../interfaces/settings/arr-instance-settings';
import { SeerrInstanceFormGroup } from '../../../interfaces/settings/seerr-instance-form-group';
import { SeerrInstanceSettings } from '../../../interfaces/settings/seerr-instance-settings';
import { AppSettingsService } from '../../../services/app-settings.service';
import { openErrorToast, openSuccessToast } from '../../../utils/feedback-toast-utils';
import { createUuidV7 } from '../../../utils/uuid-v7-utils';
import { SettingsUnsavedChangesBase } from '../settings-unsaved-changes.base';
import { SettingsArrInstanceCardWidget } from './settings-arr-instance-card/settings-arr-instance-card.widget';
import { SettingsSeerrCardWidget } from './settings-seerr-card/settings-seerr-card.widget';

const MAX_INSTANCE_FIELD_LENGTH: number = 255;

@Component({
  selector: 'app-settings-arr-instances-tab-widget',
  imports: [MatButtonModule, MatIconModule, MatSnackBarModule, SettingsArrInstanceCardWidget, SettingsSeerrCardWidget, NgOptimizedImage],
  templateUrl: './settings-arr-instances-tab.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './settings-arr-instances-tab.widget.scss',
})
export class SettingsArrInstancesTabWidget extends SettingsUnsavedChangesBase implements OnInit {
  private readonly appSettingsService: AppSettingsService = inject(AppSettingsService);
  private readonly formBuilder: NonNullableFormBuilder = inject(NonNullableFormBuilder);
  private readonly snackBar: MatSnackBar = inject(MatSnackBar);
  private instanceKeyCounter: number = 0;
  private static readonly seerrClientKey: string = 'seerr';

  readonly instancesDescription: Signal<string> = computed((): string => this.i18n.t('settings.instancesTab.description'));
  readonly sonarrHeading: Signal<string> = computed((): string => this.i18n.t('settings.sonarr'));
  readonly radarrHeading: Signal<string> = computed((): string => this.i18n.t('settings.radarr'));
  readonly seerrHeading: Signal<string> = computed((): string => this.i18n.t('settings.seerr'));
  readonly addInstanceLabel: Signal<string> = computed((): string => this.i18n.t('settings.addInstance'));
  readonly noInstanceConfiguredLabel: Signal<string> = computed((): string => this.i18n.t('common.noInstanceConfigured'));
  readonly sonarrApplicationKind: ArrApplicationKind = ARR_APPLICATION_KINDS.SONARR;
  readonly radarrApplicationKind: ArrApplicationKind = ARR_APPLICATION_KINDS.RADARR;

  protected readonly sonarrInstancesForm: FormArray<ArrInstanceFormGroup> = this.formBuilder.array<ArrInstanceFormGroup>([]);
  protected readonly radarrInstancesForm: FormArray<ArrInstanceFormGroup> = this.formBuilder.array<ArrInstanceFormGroup>([]);
  protected readonly seerrFormState: WritableSignal<SeerrInstanceFormGroup> = signal<SeerrInstanceFormGroup>(this.createSeerrFormGroup());
  protected readonly deletingState: WritableSignal<Record<string, boolean>> = signal<Record<string, boolean>>({});
  protected readonly instancesLoadedState: WritableSignal<boolean> = signal<boolean>(false);

  ngOnInit(): void {
    void this.initializeInstancesAsync();
  }

  protected instanceLogoPath(kind: ArrApplicationKind): string {
    if (kind === ARR_APPLICATION_KINDS.SEERR) {
      return '/icons/logo_seerr.svg';
    }

    return kind === ARR_APPLICATION_KINDS.SONARR ? '/icons/logo_sonarr.svg' : '/icons/logo_radarr.svg';
  }

  protected isDeleting(instanceGroup: ArrInstanceFormGroup): boolean {
    return this.deletingState()[instanceGroup.controls.clientKey.getRawValue()] ?? false;
  }

  protected addSonarrInstanceAsync(): void {
    this.addInstanceAsync(ARR_APPLICATION_KINDS.SONARR);
  }

  protected addRadarrInstanceAsync(): void {
    this.addInstanceAsync(ARR_APPLICATION_KINDS.RADARR);
  }

  protected async removeSonarrInstanceAsync(index: number): Promise<void> {
    await this.removeInstanceAsync(ARR_APPLICATION_KINDS.SONARR, index);
  }

  protected async removeRadarrInstanceAsync(index: number): Promise<void> {
    await this.removeInstanceAsync(ARR_APPLICATION_KINDS.RADARR, index);
  }

  protected isSeerrConfigured(): boolean {
    return this.appSettingsService.seerrInstance() !== null;
  }

  protected isSeerrDeleting(): boolean {
    return this.deletingState()[SettingsArrInstancesTabWidget.seerrClientKey] ?? false;
  }

  protected async removeSeerrInstanceAsync(): Promise<void> {
    if (this.appSettingsService.seerrInstance() === null) {
      this.syncSeerrFromService();
      return;
    }

    this.deletingState.update((current) => ({ ...current, [SettingsArrInstancesTabWidget.seerrClientKey]: true }));

    try {
      await this.appSettingsService.deleteSeerrInstance();
      this.syncSeerrFromService();
      openSuccessToast(this.snackBar, this.i18n.t('settings.deleteSuccess'));
    } catch (error) {
      const message: string =
        error instanceof Error && error.message.trim().length > 0 ? error.message.trim() : this.i18n.t('settings.deleteError');
      openErrorToast(this.snackBar, message);
    } finally {
      this.deletingState.update((current) => ({ ...current, [SettingsArrInstancesTabWidget.seerrClientKey]: false }));
    }
  }

  private async initializeInstancesAsync(): Promise<void> {
    void this.initializeSeerrAsync();

    try {
      await this.appSettingsService.loadInstances();
      this.syncInstancesFromService();
    } catch {
      // Keep the existing/default instance list until the next successful load.
    }

    queueMicrotask(() => {
      this.instancesLoadedState.set(true);
    });
  }

  private async initializeSeerrAsync(): Promise<void> {
    try {
      await this.appSettingsService.loadSeerrInstance();
      this.syncSeerrFromService();
    } catch {
      // Keep the existing/default Seerr form state until the next successful load.
    }
  }

  private addInstanceAsync(kind: ArrApplicationKind): void {
    const instances: FormArray<ArrInstanceFormGroup> =
      kind === ARR_APPLICATION_KINDS.SONARR ? this.sonarrInstancesForm : this.radarrInstancesForm;

    instances.push(this.createInstanceFormGroup());
    instances.markAsDirty();
  }

  private async removeInstanceAsync(kind: ArrApplicationKind, index: number): Promise<void> {
    const instances: FormArray<ArrInstanceFormGroup> =
      kind === ARR_APPLICATION_KINDS.SONARR ? this.sonarrInstancesForm : this.radarrInstancesForm;
    const instanceGroup: ArrInstanceFormGroup | undefined = instances.at(index);
    if (!instanceGroup) {
      return;
    }

    const instanceId: string = instanceGroup.controls.id.getRawValue().trim();
    if (!this.isPersistedInstance(kind, index, instanceId)) {
      instances.removeAt(index);
      instances.markAsPristine();
      return;
    }

    const instanceKey: string = instanceGroup.controls.clientKey.getRawValue();
    this.deletingState.update((current) => ({ ...current, [instanceKey]: true }));

    try {
      if (kind === ARR_APPLICATION_KINDS.SONARR) {
        await this.appSettingsService.deleteSonarrInstance(index);
      } else {
        await this.appSettingsService.deleteRadarrInstance(index);
      }

      this.syncInstancesFromService();
      openSuccessToast(this.snackBar, this.i18n.t('settings.deleteSuccess'));
    } catch (error) {
      const message: string =
        error instanceof Error && error.message.trim().length > 0 ? error.message.trim() : this.i18n.t('settings.deleteError');
      openErrorToast(this.snackBar, message);
    } finally {
      this.deletingState.update((current) => ({ ...current, [instanceKey]: false }));
    }
  }

  private isPersistedInstance(kind: ArrApplicationKind, index: number, instanceId: string): boolean {
    if (instanceId.length === 0) {
      return false;
    }

    const persistedInstances: readonly ArrInstanceSettings[] =
      kind === ARR_APPLICATION_KINDS.SONARR ? this.appSettingsService.sonarrInstances() : this.appSettingsService.radarrInstances();
    const persistedInstance: ArrInstanceSettings | undefined = persistedInstances[index];
    if (!persistedInstance) {
      return false;
    }

    return persistedInstance.id.trim() === instanceId;
  }

  private createInstanceFormGroup(instance?: ArrInstanceSettings): ArrInstanceFormGroup {
    const instanceId: string = instance?.id ?? createUuidV7();
    return this.formBuilder.group({
      clientKey: this.formBuilder.control<string>(this.buildClientKey(instance)),
      id: this.formBuilder.control<string>(instanceId),
      name: this.formBuilder.control<string>(instance?.name ?? '', [Validators.required, Validators.maxLength(MAX_INSTANCE_FIELD_LENGTH)]),
      baseUrl: this.formBuilder.control<string>(instance?.baseUrl ?? '', [
        Validators.required,
        Validators.maxLength(MAX_INSTANCE_FIELD_LENGTH),
      ]),
      apiKey: this.formBuilder.control<string>(instance?.apiKey ?? '', [
        Validators.required,
        Validators.maxLength(MAX_INSTANCE_FIELD_LENGTH),
      ]),
      testSucceeded: this.formBuilder.control<boolean>(instance !== undefined),
      testMessage: this.formBuilder.control<string>(''),
      lastTestedFingerprint: this.formBuilder.control<string>(instance === undefined ? '' : this.buildFingerprint(instance)),
      syncMessage: this.formBuilder.control<string>(''),
    });
  }

  private buildClientKey(instance?: ArrInstanceSettings): string {
    if (instance?.id) {
      return instance.id;
    }

    const clientKey: string = `instance-${this.instanceKeyCounter}`;
    this.instanceKeyCounter += 1;

    return clientKey;
  }

  private createSeerrFormGroup(instance?: SeerrInstanceSettings): SeerrInstanceFormGroup {
    return this.formBuilder.group({
      clientKey: this.formBuilder.control<string>(SettingsArrInstancesTabWidget.seerrClientKey),
      id: this.formBuilder.control<string>(instance?.id ?? ''),
      baseUrl: this.formBuilder.control<string>(instance?.baseUrl ?? '', [
        Validators.required,
        Validators.maxLength(MAX_INSTANCE_FIELD_LENGTH),
      ]),
      apiKey: this.formBuilder.control<string>(instance?.apiKey ?? '', [
        Validators.required,
        Validators.maxLength(MAX_INSTANCE_FIELD_LENGTH),
      ]),
      testSucceeded: this.formBuilder.control<boolean>(instance !== undefined),
      testMessage: this.formBuilder.control<string>(''),
      lastTestedFingerprint: this.formBuilder.control<string>(instance === undefined ? '' : this.buildFingerprint(instance)),
    });
  }

  private syncSeerrFromService(): void {
    this.seerrFormState.set(this.createSeerrFormGroup(this.appSettingsService.seerrInstance() ?? undefined));
  }

  private buildFingerprint(instance: { name?: string; baseUrl: string; apiKey: string }): string {
    return JSON.stringify({
      name: instance.name,
      baseUrl: instance.baseUrl,
      apiKey: instance.apiKey,
    });
  }

  protected override isBusy(): boolean {
    return this.isDeletionPending();
  }

  protected override hasUnsavedChanges(): boolean {
    return (
      [...this.sonarrInstancesForm.controls, ...this.radarrInstancesForm.controls].some(
        (instanceGroup) => instanceGroup.dirty || instanceGroup.controls.id.getRawValue().trim().length === 0,
      ) || this.seerrFormState().dirty
    );
  }

  private isDeletionPending(): boolean {
    return Object.values(this.deletingState()).some(Boolean);
  }

  protected override discardUnsavedChanges(): void {
    this.syncInstancesFromService();
    this.syncSeerrFromService();
    this.sonarrInstancesForm.markAsPristine();
    this.radarrInstancesForm.markAsPristine();
  }

  private syncInstancesFromService(): void {
    this.replaceInstances(this.sonarrInstancesForm, this.appSettingsService.sonarrInstances());
    this.replaceInstances(this.radarrInstancesForm, this.appSettingsService.radarrInstances());
    this.deletingState.set({});
  }

  private replaceInstances(target: FormArray<ArrInstanceFormGroup>, instances: readonly ArrInstanceSettings[]): void {
    target.clear();

    for (const instance of instances) {
      target.push(this.createInstanceFormGroup(instance));
    }

    target.markAsPristine();
  }

  protected override collectUnsavedChangeDescriptions(): string[] {
    const descriptions: string[] = [];

    for (const [label, instances] of [
      [this.i18n.t('settings.radarr'), this.radarrInstancesForm.controls],
      [this.i18n.t('settings.sonarr'), this.sonarrInstancesForm.controls],
    ] as const) {
      for (let index: number = 0; index < instances.length; index += 1) {
        const instanceGroup: ArrInstanceFormGroup = instances[index];
        const description: string | null = this.buildInstanceUnsavedChangeDescription(label, index, instanceGroup);
        if (description !== null) {
          descriptions.push(description);
        }
      }
    }

    const seerrDescription: string | null = this.buildSeerrUnsavedChangeDescription();
    if (seerrDescription !== null) {
      descriptions.push(seerrDescription);
    }

    return descriptions;
  }

  private buildInstanceUnsavedChangeDescription(label: string, index: number, instanceGroup: ArrInstanceFormGroup): string | null {
    if (this.isNewInstance(instanceGroup)) {
      return `${label} ${index + 1}: ${this.i18n.t('settings.unsavedChangesNewInstance')}`;
    }

    const changedFields: string[] = this.collectChangedInstanceFields(instanceGroup);
    if (changedFields.length === 0) {
      return null;
    }

    return `${label} ${index + 1}: ${changedFields.join(', ')}`;
  }

  private buildSeerrUnsavedChangeDescription(): string | null {
    const seerrForm: SeerrInstanceFormGroup = this.seerrFormState();
    if (!seerrForm.dirty) {
      return null;
    }

    if (seerrForm.controls.id.getRawValue().trim().length === 0) {
      return `${this.i18n.t('settings.seerr')}: ${this.i18n.t('settings.unsavedChangesNewInstance')}`;
    }

    const changedFields: string[] = this.collectChangedConnectionFields(seerrForm);
    if (changedFields.length === 0) {
      return null;
    }

    return `${this.i18n.t('settings.seerr')}: ${changedFields.join(', ')}`;
  }

  private collectChangedInstanceFields(instanceGroup: ArrInstanceFormGroup): string[] {
    const changedFields: string[] = [];
    if (instanceGroup.controls.name.dirty) {
      changedFields.push(this.i18n.t('settings.instanceName'));
    }

    changedFields.push(...this.collectChangedConnectionFields(instanceGroup));

    return changedFields;
  }

  private collectChangedConnectionFields(instanceGroup: ArrInstanceFormGroup | SeerrInstanceFormGroup): string[] {
    const changedFields: string[] = [];
    if (instanceGroup.controls.baseUrl.dirty) {
      changedFields.push(this.i18n.t('settings.instanceUrl'));
    }
    if (instanceGroup.controls.apiKey.dirty) {
      changedFields.push(this.i18n.t('settings.instanceApiKey'));
    }

    return changedFields;
  }

  private isNewInstance(instanceGroup: ArrInstanceFormGroup): boolean {
    return instanceGroup.controls.id.getRawValue().trim().length === 0;
  }
}
