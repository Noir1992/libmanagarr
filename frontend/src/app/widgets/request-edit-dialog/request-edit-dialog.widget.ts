import { ChangeDetectionStrategy, Component, computed, inject, Signal, signal, WritableSignal } from '@angular/core';
import { FormControl, NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { SeerrRequestEditOptions, SeerrSeasonOption } from '../../interfaces/requests/seerr-request-edit-options';
import { SeerrRequestUpdate } from '../../interfaces/requests/seerr-request-update';
import { AppI18nService } from '../../services/app-i18n.service';

export interface RequestEditDialogData {
  readonly title: string | null;
  readonly options: SeerrRequestEditOptions;
}

@Component({
  selector: 'app-request-edit-dialog',
  imports: [ReactiveFormsModule, MatButtonModule, MatDialogModule, MatSelectModule, MatSlideToggleModule],
  templateUrl: './request-edit-dialog.widget.html',
  styleUrl: './request-edit-dialog.widget.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RequestEditDialogWidget {
  protected readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly dialogRef: MatDialogRef<RequestEditDialogWidget> = inject(MatDialogRef<RequestEditDialogWidget>);
  readonly data: RequestEditDialogData = inject(MAT_DIALOG_DATA);
  private readonly formBuilder: NonNullableFormBuilder = inject(NonNullableFormBuilder);

  // Tv requests pick their seasons in a table. A season that is still downloading or that is
  // already on disk cannot be requested again, so its switch is locked and only shows the
  // current state. The seasons this request covers are being edited right now, so their switch
  // stays enabled and preselected.
  private readonly seasonControls: readonly FormControl<boolean>[] = this.data.options.seasons.map(
    (season: SeerrSeasonOption): FormControl<boolean> =>
      this.formBuilder.control({
        value: season.state === 'pending',
        disabled: this.isSeasonLocked(season),
      }),
  );

  protected readonly form = this.formBuilder.group({
    profileId: this.formBuilder.control<number | null>(this.initialProfileId()),
    rootFolder: this.formBuilder.control<string | null>(this.initialRootFolder()),
    seasons: this.formBuilder.array([...this.seasonControls]),
  });

  protected readonly titleLabel: Signal<string> = computed((): string => this.i18n.t('requests.edit.title'));
  protected readonly profileLabel: Signal<string> = computed((): string => this.i18n.t('requests.edit.profile'));
  protected readonly rootFolderLabel: Signal<string> = computed((): string => this.i18n.t('requests.edit.rootFolder'));
  protected readonly seasonsLabel: Signal<string> = computed((): string => this.i18n.t('requests.seasons'));
  protected readonly advancedLabel: Signal<string> = computed((): string => this.i18n.t('requests.edit.advanced'));
  protected readonly seasonColumnLabel: Signal<string> = computed((): string => this.i18n.t('common.column.season'));
  protected readonly episodeColumnLabel: Signal<string> = computed((): string => this.i18n.t('common.episodeCount'));
  protected readonly statusColumnLabel: Signal<string> = computed((): string => this.i18n.t('requests.status'));
  protected readonly saveLabel: Signal<string> = computed((): string => this.i18n.t('requests.edit.saveAndApprove'));
  protected readonly cancelLabel: Signal<string> = computed((): string => this.i18n.t('common.cancel'));

  protected readonly canSave: WritableSignal<boolean> = signal<boolean>(false);

  constructor() {
    this.form.valueChanges.subscribe((): void => this.canSave.set(this.hasCompleteSelection()));
    this.canSave.set(this.hasCompleteSelection());
  }

  // Season 0 carries the specials and is labeled like in the Sonarr dialogs.
  protected seasonLabel(season: SeerrSeasonOption): string {
    return season.number === 0
      ? this.i18n.t('filter.episodeSpecial')
      : `${this.i18n.t('common.column.season')} ${season.number}`;
  }

  protected episodeCountLabel(season: SeerrSeasonOption): string {
    return season.episodeCount === null ? '' : String(season.episodeCount);
  }

  // A season nothing covers yet shows "Not requested"; the seasons of the request being edited
  // show the state they will get once the update is approved.
  protected seasonStateLabel(season: SeerrSeasonOption): string {
    switch (season.state) {
      case 'pending':
        return this.i18n.t('requests.seasonState.pending');
      case 'processing':
        return this.i18n.t('requests.seasonState.processing');
      case 'available':
        return this.i18n.t('requests.seasonState.available');
      default:
        return this.i18n.t('requests.seasonState.none');
    }
  }

  protected isSeasonLocked(season: SeerrSeasonOption): boolean {
    return season.state === 'processing' || season.state === 'available';
  }

  protected cancel(): void {
    this.dialogRef.close();
  }

  protected save(): void {
    const { profileId, rootFolder } = this.form.getRawValue();
    if (profileId === null || rootFolder === null) {
      return;
    }

    if (this.seasonControls.length > 0 && this.selectedSeasons().length === 0) {
      return;
    }

    const update: SeerrRequestUpdate = { profileId, rootFolder };
    if (this.seasonControls.length > 0) {
      update.seasons = this.selectedSeasons();
    }

    this.dialogRef.close(update);
  }

  // Pending requests carry no values of their own, so the server defaults preselect; a
  // missing default still leaves the first option chosen because saving requires a value.
  private initialProfileId(): number | null {
    return this.data.options.profileId ?? this.data.options.qualityProfiles[0]?.id ?? null;
  }

  private initialRootFolder(): string | null {
    return this.data.options.rootFolder ?? this.data.options.rootFolders[0] ?? null;
  }

  private hasCompleteSelection(): boolean {
    const { profileId, rootFolder } = this.form.getRawValue();
    if (profileId === null || rootFolder === null) {
      return false;
    }

    return this.seasonControls.length === 0 || this.selectedSeasons().length > 0;
  }

  // Disabled switches keep their value, so the locked seasons travel along in the update body.
  private selectedSeasons(): number[] {
    return this.data.options.seasons
      .filter((season: SeerrSeasonOption, index: number): boolean => this.seasonControls[index].value === true)
      .map((season: SeerrSeasonOption): number => season.number);
  }
}
