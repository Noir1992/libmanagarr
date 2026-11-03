import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, Signal, signal, WritableSignal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ReleaseOverview } from '../interfaces/releases/release-overview';
import { ReleaseVersion } from '../interfaces/releases/release-version';
import { AppI18nService } from './app-i18n.service';

@Injectable({ providedIn: 'root' })
export class ReleasesService {
  private readonly http: HttpClient = inject(HttpClient);
  private readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly apiUrl: string = '/api/releases';
  private readonly overviewState: WritableSignal<ReleaseOverview | null> = signal<ReleaseOverview | null>(null);
  private readonly loadingState: WritableSignal<boolean> = signal<boolean>(false);
  private readonly loadErrorState: WritableSignal<string | null> = signal<string | null>(null);
  private attempted: boolean = false;
  readonly overview: Signal<ReleaseOverview | null> = computed((): ReleaseOverview | null => this.overviewState());
  readonly loading: Signal<boolean> = computed((): boolean => this.loadingState());
  readonly loadError: Signal<string | null> = computed((): string | null => this.loadErrorState());
  readonly versions: Signal<readonly ReleaseVersion[]> = computed((): readonly ReleaseVersion[] => this.overviewState()?.versions ?? []);
  readonly currentVersion: Signal<string> = computed((): string => this.overviewState()?.currentVersion ?? '');
  readonly updateAvailable: Signal<boolean> = computed((): boolean => this.overviewState()?.updateAvailable ?? false);

  async loadAsync(refresh: boolean): Promise<void> {
    if (this.loadingState()) {
      return;
    }

    this.loadingState.set(true);
    this.loadErrorState.set(null);

    try {
      const overview: ReleaseOverview = await firstValueFrom(
        refresh ? this.http.post<ReleaseOverview>(`${this.apiUrl}/refresh`, {}) : this.http.get<ReleaseOverview>(`${this.apiUrl}/`),
      );
      this.overviewState.set(overview);
    } catch {
      this.loadErrorState.set(this.i18n.t('releases.unreachable'));
    } finally {
      this.attempted = true;
      this.loadingState.set(false);
    }
  }

  // The navigation shows the version on every shell render, so the first answer is kept and
  // refreshed only when the page asks for it.
  async loadOnceAsync(): Promise<void> {
    if (this.attempted || this.overviewState() !== null) {
      return;
    }

    await this.loadAsync(false);
  }
}
