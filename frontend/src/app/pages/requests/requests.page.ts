import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnInit,
  Signal,
} from '@angular/core';
import { MatIcon } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { SeerrRequest, SeerrRequestStatus, SeerrRequestType } from '../../interfaces/requests/seerr-request';
import { AppI18nService } from '../../services/app-i18n.service';
import { TranslationKey } from '../../services/i18n/_i18n-key';
import { RequestsService } from '../../services/requests.service';
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
const FILTERABLE_STATUSES: readonly Exclude<SeerrRequestStatus, 'unknown'>[] = [
  'pending',
  'approved',
  'processing',
  'available',
  'failed',
];

@Component({
  selector: 'app-requests-page',
  imports: [DatePipe, MatIcon, RouterLink, LoadingErrorSectionWidget, LoadingSectionWidget, NoDataSectionWidget],
  templateUrl: './requests.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './requests.page.scss',
})
export class RequestsPage implements OnInit {
  private readonly requestsService: RequestsService = inject(RequestsService);
  private readonly i18n: AppI18nService = inject(AppI18nService);

  /* I18n signals */
  protected readonly title: Signal<string> = computed((): string => this.i18n.t('requests.title'));
  protected readonly description: Signal<string> = computed((): string => this.i18n.t('requests.description'));
  protected readonly notConfiguredText: Signal<string> = computed((): string => this.i18n.t('requests.notConfigured'));
  protected readonly goToSettingsText: Signal<string> = computed((): string => this.i18n.t('requests.goToSettings'));
  protected readonly requestedByLabel: Signal<string> = computed((): string => this.i18n.t('requests.requestedBy'));
  protected readonly previousLabel: Signal<string> = computed((): string => this.i18n.t('requests.previous'));
  protected readonly nextLabel: Signal<string> = computed((): string => this.i18n.t('requests.next'));

  /* State signals */
  protected readonly requests = this.requestsService.requests;
  protected readonly loading = this.requestsService.loading;
  protected readonly loadError = this.requestsService.loadError;
  protected readonly notConfigured = this.requestsService.notConfigured;
  protected readonly statusFilter = this.requestsService.statusFilter;
  protected readonly statusFilters: Signal<readonly RequestsFilterOption[]> = computed(
    (): readonly RequestsFilterOption[] => [
      { value: null, label: this.i18n.t('requests.filter.all') },
      ...FILTERABLE_STATUSES.map(
        (status: Exclude<SeerrRequestStatus, 'unknown'>): RequestsFilterOption => ({
          value: status,
          label: this.i18n.t(STATUS_LABEL_KEYS[status]),
        }),
      ),
    ],
  );
  protected readonly isFiltered: Signal<boolean> = computed((): boolean => this.statusFilter() !== null);
  protected readonly totalPages: Signal<number> = computed(
    (): number => Math.max(1, Math.ceil(this.requestsService.total() / Math.max(1, this.requestsService.limit()))),
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

  protected posterUrl(request: SeerrRequest): string | null {
    const posterPath: string | null = request.posterPath;
    if (posterPath === null) {
      return null;
    }

    // This app's poster cache endpoint and absolute urls render directly; other relative paths
    // belong to a foreign base url and keep the placeholder icon instead.
    if (
      posterPath.startsWith('/api/') ||
      posterPath.startsWith('http://') ||
      posterPath.startsWith('https://')
    ) {
      return posterPath;
    }

    return null;
  }
}
