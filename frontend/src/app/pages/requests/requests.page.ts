import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, OnInit, Signal, signal, WritableSignal } from '@angular/core';
import { MatIcon } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { SeerrRequest, SeerrRequestStatus, SeerrRequestType } from '../../interfaces/requests/seerr-request';
import { SeerrRequestEditOptions } from '../../interfaces/requests/seerr-request-edit-options';
import { SeerrRequestUpdate } from '../../interfaces/requests/seerr-request-update';
import { AppI18nService } from '../../services/app-i18n.service';
import { TranslationKey } from '../../services/i18n/_i18n-key';
import { RequestsService } from '../../services/requests.service';
import { openErrorToast, openSuccessToast } from '../../utils/feedback-toast-utils';
import { RequestDeclineDialogWidget } from '../../widgets/request-decline-dialog/request-decline-dialog.widget';
import { RequestDeleteDialogWidget } from '../../widgets/request-delete-dialog/request-delete-dialog.widget';
import { RequestEditDialogWidget } from '../../widgets/request-edit-dialog/request-edit-dialog.widget';
import { LoadingErrorSectionWidget } from '../../widgets/sections/loading-error-section/loading-error-section.widget';
import { LoadingSectionWidget } from '../../widgets/sections/loading-section/loading-section.widget';
import { NoDataSectionWidget } from '../../widgets/sections/no-data-section/no-data-section.widget';

interface RequestsFilterOption {
  readonly value: SeerrRequestStatus | null;
  readonly label: string;
}

const STATUS_LABEL_KEYS: Record<SeerrRequestStatus, TranslationKey> = {
  pending: 'requests.status.pending',
  approved: 'requests.status.approved',
  declined: 'requests.status.declined',
  processing: 'requests.status.processing',
  available: 'requests.status.available',
  failed: 'requests.status.failed',
  unknown: 'requests.status.unknown',
};

// Seerr's filter parameter has no "declined" value, so the chip bar omits it even though
// status 3 rows still render their declined label.
const FILTERABLE_STATUSES: readonly Exclude<SeerrRequestStatus, 'unknown'>[] = ['pending', 'approved', 'processing', 'available', 'failed'];

@Component({
  selector: 'app-requests-page',
  imports: [DatePipe, MatIcon, MatMenuModule, RouterLink, LoadingErrorSectionWidget, LoadingSectionWidget, NoDataSectionWidget],
  templateUrl: './requests.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './requests.page.scss',
})
export class RequestsPage implements OnInit {
  private readonly requestsService: RequestsService = inject(RequestsService);
  private readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly dialog: MatDialog = inject(MatDialog);
  private readonly snackBar: MatSnackBar = inject(MatSnackBar);
  private readonly runningActionState: WritableSignal<boolean> = signal<boolean>(false);

  /* I18n signals */
  protected readonly title: Signal<string> = computed((): string => this.i18n.t('requests.title'));
  protected readonly description: Signal<string> = computed((): string => this.i18n.t('requests.description'));
  protected readonly notConfiguredText: Signal<string> = computed((): string => this.i18n.t('requests.notConfigured'));
  protected readonly goToSettingsText: Signal<string> = computed((): string => this.i18n.t('requests.goToSettings'));
  protected readonly requestedByLabel: Signal<string> = computed((): string => this.i18n.t('requests.requestedBy'));
  protected readonly requestedAtLabel: Signal<string> = computed((): string => this.i18n.t('requests.requestedAt'));
  protected readonly seasonsLabel: Signal<string> = computed((): string => this.i18n.t('requests.seasons'));
  protected readonly statusHeadingLabel: Signal<string> = computed((): string => this.i18n.t('requests.status'));
  protected readonly previousLabel: Signal<string> = computed((): string => this.i18n.t('requests.previous'));
  protected readonly nextLabel: Signal<string> = computed((): string => this.i18n.t('requests.next'));
  protected readonly approveLabel: Signal<string> = computed((): string => this.i18n.t('requests.action.approve'));
  protected readonly declineLabel: Signal<string> = computed((): string => this.i18n.t('requests.action.decline'));
  protected readonly editLabel: Signal<string> = computed((): string => this.i18n.t('requests.action.edit'));
  protected readonly deleteLabel: Signal<string> = computed((): string => this.i18n.t('requests.action.delete'));
  protected readonly moreActionsLabel: Signal<string> = computed((): string => this.i18n.t('requests.action.more'));
  protected readonly runningAction: Signal<boolean> = computed((): boolean => this.runningActionState());

  /* State signals */
  protected readonly requests = this.requestsService.requests;
  protected readonly loading = this.requestsService.loading;
  protected readonly loadError = this.requestsService.loadError;
  protected readonly notConfigured = this.requestsService.notConfigured;
  protected readonly statusFilter = this.requestsService.statusFilter;
  protected readonly statusFilters: Signal<readonly RequestsFilterOption[]> = computed((): readonly RequestsFilterOption[] => [
    { value: null, label: this.i18n.t('requests.filter.all') },
    ...FILTERABLE_STATUSES.map((status: Exclude<SeerrRequestStatus, 'unknown'>): RequestsFilterOption => ({
      value: status,
      label: this.i18n.t(STATUS_LABEL_KEYS[status]),
    })),
  ]);
  protected readonly isFiltered: Signal<boolean> = computed((): boolean => this.statusFilter() !== null);
  protected readonly totalPages: Signal<number> = computed((): number =>
    Math.max(1, Math.ceil(this.requestsService.total() / Math.max(1, this.requestsService.limit()))),
  );
  protected readonly canGoPrevious: Signal<boolean> = computed((): boolean => this.requestsService.page() > 1);
  protected readonly canGoNext: Signal<boolean> = computed((): boolean => this.requestsService.page() < this.totalPages());
  protected readonly paginationInfo: Signal<string> = computed((): string =>
    this.i18n.t('requests.paginationInfo', {
      page: String(this.requestsService.page()),
      totalPages: String(this.totalPages()),
    }),
  );

  ngOnInit(): void {
    void this.requestsService.loadAsync();
  }

  protected isActiveFilter(value: SeerrRequestStatus | null): boolean {
    return this.statusFilter() === value;
  }

  protected applyStatusFilter(value: SeerrRequestStatus | null): void {
    if (this.isActiveFilter(value)) {
      return;
    }

    void this.requestsService.setStatusFilterAsync(value);
  }

  protected previousPage(): void {
    void this.requestsService.setPageAsync(this.requestsService.page() - 1);
  }

  protected nextPage(): void {
    void this.requestsService.setPageAsync(this.requestsService.page() + 1);
  }

  protected canApproveOrDecline(request: SeerrRequest): boolean {
    return request.status === 'pending';
  }

  // Editing configures the request before approval; approved rows are configured already.
  protected canEdit(request: SeerrRequest): boolean {
    return (request.type === 'movie' || request.type === 'tv') && request.status === 'pending';
  }

  protected async approveAsync(request: SeerrRequest): Promise<void> {
    await this.runActionAsync(
      async (): Promise<void> => await this.requestsService.approveAsync(Number(request.id)),
      'requests.action.approveSuccess',
    );
  }

  protected async declineWithDialogAsync(request: SeerrRequest): Promise<void> {
    const reason: string | undefined = await firstValueFrom(
      this.dialog.open(RequestDeclineDialogWidget, { data: { title: request.title }, width: '480px', maxWidth: '95vw' }).afterClosed(),
    );

    if (reason === undefined) {
      return;
    }

    await this.runActionAsync(
      async (): Promise<void> => await this.requestsService.declineAsync(Number(request.id), reason),
      'requests.action.declineSuccess',
    );
  }

  protected async deleteWithDialogAsync(request: SeerrRequest): Promise<void> {
    const confirmed: boolean | undefined = await firstValueFrom(
      this.dialog.open(RequestDeleteDialogWidget, { data: { title: request.title }, width: '480px', maxWidth: '95vw' }).afterClosed(),
    );

    if (confirmed !== true) {
      return;
    }

    await this.runActionAsync(
      async (): Promise<void> => await this.requestsService.deleteAsync(Number(request.id)),
      'requests.action.deleteSuccess',
    );
  }

  protected async editWithDialogAsync(request: SeerrRequest): Promise<void> {
    if (request.type === null) {
      return;
    }

    let options: SeerrRequestEditOptions;
    try {
      options = await this.requestsService.loadEditOptionsAsync(Number(request.id));
    } catch (error: unknown) {
      openErrorToast(this.snackBar, this.requestsService.describeActionError(error));
      return;
    }

    const update: SeerrRequestUpdate | undefined = await firstValueFrom(
      this.dialog
        .open(RequestEditDialogWidget, {
          data: { title: request.title, options },
          width: '480px',
          maxWidth: '95vw',
        })
        .afterClosed(),
    );

    if (update === undefined) {
      return;
    }

    // Saving is not a plain edit: like in Seerr the adjusted request is approved right away.
    await this.runActionAsync(async (): Promise<void> => {
      await this.requestsService.updateAsync(Number(request.id), update);
      await this.requestsService.approveAsync(Number(request.id));
    }, 'requests.action.approveSuccess');
  }

  private async runActionAsync(action: () => Promise<void>, successKey: TranslationKey): Promise<void> {
    if (this.runningActionState()) {
      return;
    }

    this.runningActionState.set(true);
    try {
      await action();
      openSuccessToast(this.snackBar, this.i18n.t(successKey));
      await this.requestsService.loadAsync();
    } catch (error: unknown) {
      openErrorToast(this.snackBar, this.requestsService.describeActionError(error));
    } finally {
      this.runningActionState.set(false);
    }
  }

  protected statusLabel(status: SeerrRequestStatus): string {
    return this.i18n.t(STATUS_LABEL_KEYS[status]);
  }

  protected typeLabel(type: SeerrRequestType | null): string | null {
    if (type === null) {
      return null;
    }

    return this.i18n.t(type === 'movie' ? 'requests.type.movie' : 'requests.type.tv');
  }

  protected typeIcon(type: SeerrRequestType | null): string {
    if (type === 'movie') {
      return 'movie';
    }

    return type === 'tv' ? 'tv' : 'help';
  }

  // Season 0 carries the specials and is labeled like in the Sonarr dialogs.
  protected seasonLabel(season: number): string {
    return season === 0 ? this.i18n.t('filter.episodeSpecial') : String(season);
  }

  protected posterUrl(request: SeerrRequest): string | null {
    const posterPath: string | null = request.posterPath;
    if (posterPath === null) {
      return null;
    }

    // This app's poster cache endpoint and absolute urls render directly; other relative paths
    // belong to a foreign base url and keep the placeholder icon instead.
    if (posterPath.startsWith('/api/') || posterPath.startsWith('http://') || posterPath.startsWith('https://')) {
      return posterPath;
    }

    return null;
  }
}
