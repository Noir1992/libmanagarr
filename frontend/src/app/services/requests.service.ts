import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { computed, inject, Injectable, Signal, signal, WritableSignal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { SeerrRequest } from '../interfaces/requests/seerr-request';
import { SeerrRequestsResponse } from '../interfaces/requests/seerr-requests-response';
import { AppI18nService } from './app-i18n.service';

const DEFAULT_PAGE: number = 1;
const DEFAULT_LIMIT: number = 20;
const DEFAULT_SORT_FIELD: string = 'createdAt';
const DEFAULT_SORT_ORDER: string = 'desc';
const NOT_CONFIGURED_PROBLEM_CODE: string = 'seerr-not-configured';

@Injectable({ providedIn: 'root' })
export class RequestsService {
  private readonly http: HttpClient = inject(HttpClient);
  private readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly apiUrl: string = '/api/requests/';
  private readonly requestsState: WritableSignal<readonly SeerrRequest[]> = signal<readonly SeerrRequest[]>([]);
  private readonly totalState: WritableSignal<number> = signal<number>(0);
  private readonly pageState: WritableSignal<number> = signal<number>(DEFAULT_PAGE);
  private readonly limitState: WritableSignal<number> = signal<number>(DEFAULT_LIMIT);
  private readonly statusFilterState: WritableSignal<string | null> = signal<string | null>(null);
  private readonly sortFieldState: WritableSignal<string> = signal<string>(DEFAULT_SORT_FIELD);
  private readonly sortOrderState: WritableSignal<string> = signal<string>(DEFAULT_SORT_ORDER);
  private readonly loadingState: WritableSignal<boolean> = signal<boolean>(false);
  private readonly loadErrorState: WritableSignal<string | null> = signal<string | null>(null);
  private readonly notConfiguredState: WritableSignal<boolean> = signal<boolean>(false);
  readonly requests: Signal<readonly SeerrRequest[]> = computed((): readonly SeerrRequest[] => this.requestsState());
  readonly total: Signal<number> = computed((): number => this.totalState());
  readonly page: Signal<number> = computed((): number => this.pageState());
  readonly limit: Signal<number> = computed((): number => this.limitState());
  readonly statusFilter: Signal<string | null> = computed((): string | null => this.statusFilterState());
  readonly sortField: Signal<string> = computed((): string => this.sortFieldState());
  readonly sortOrder: Signal<string> = computed((): string => this.sortOrderState());
  readonly loading: Signal<boolean> = computed((): boolean => this.loadingState());
  readonly loadError: Signal<string | null> = computed((): string | null => this.loadErrorState());
  readonly notConfigured: Signal<boolean> = computed((): boolean => this.notConfiguredState());

  async loadAsync(): Promise<void> {
    this.loadingState.set(true);
    this.loadErrorState.set(null);
    this.notConfiguredState.set(false);

    try {
      let params: HttpParams = new HttpParams()
        .set('page', this.pageState())
        .set('limit', this.limitState())
        .set('sortField', this.sortFieldState())
        .set('sortOrder', this.sortOrderState());

      const statusFilter: string | null = this.statusFilterState();
      if (statusFilter !== null) {
        params = params.set('status', statusFilter);
      }

      const response: SeerrRequestsResponse = await firstValueFrom(
        this.http.get<SeerrRequestsResponse>(this.apiUrl, { params }),
      );

      this.requestsState.set(response.requests ?? []);
      this.totalState.set(response.total ?? 0);
      this.pageState.set(response.page ?? this.pageState());
      this.limitState.set(response.limit ?? this.limitState());
    } catch (error: unknown) {
      this.requestsState.set([]);
      this.totalState.set(0);

      if (this.isNotConfiguredError(error)) {
        this.notConfiguredState.set(true);
      } else {
        this.loadErrorState.set(this.readErrorMessage(error));
      }
    } finally {
      this.loadingState.set(false);
    }
  }

  async setStatusFilterAsync(status: string | null): Promise<void> {
    this.statusFilterState.set(status);
    this.pageState.set(DEFAULT_PAGE);
    await this.loadAsync();
  }

  async setPageAsync(page: number): Promise<void> {
    if (page < DEFAULT_PAGE) {
      return;
    }

    this.pageState.set(page);
    await this.loadAsync();
  }

  private isNotConfiguredError(error: unknown): boolean {
    if (!(error instanceof HttpErrorResponse)) {
      return false;
    }

    const code: unknown = (error.error as { code?: unknown } | null)?.['code'];

    return code === NOT_CONFIGURED_PROBLEM_CODE || error.status === 503;
  }

  private readErrorMessage(error: unknown): string {
    const fallbackMessage: string = this.i18n.t('requests.loadError');

    if (error instanceof HttpErrorResponse) {
      const body: { detail?: unknown; title?: unknown } | null =
        error.error && typeof error.error === 'object' ? (error.error as { detail?: unknown; title?: unknown }) : null;
      const detail: string | null = typeof body?.detail === 'string' ? body.detail.trim() : null;
      if (detail !== null && detail.length > 0) {
        return detail;
      }

      const title: string | null = typeof body?.title === 'string' ? body.title.trim() : null;
      if (title !== null && title.length > 0) {
        return title;
      }
    }

    return fallbackMessage;
  }
}
