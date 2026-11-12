import { formatDate } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, OnInit, Signal } from '@angular/core';
import { MatIcon } from '@angular/material/icon';
import { ReleaseOverview } from '../../interfaces/releases/release-overview';
import { ReleaseVersion } from '../../interfaces/releases/release-version';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { ReleasesService } from '../../services/releases.service';
import { TranslationKey } from '../../services/i18n/_i18n-key';
import { MarkdownPipe } from '../../pipes/markdown.pipe';
import { LoadingErrorSectionWidget } from '../../widgets/sections/loading-error-section/loading-error-section.widget';
import { LoadingSectionWidget } from '../../widgets/sections/loading-section/loading-section.widget';

const STATUS_NOTICE_KEYS: Record<Exclude<ReleaseOverview['status'], 'Ok'>, TranslationKey> = {
  Unreachable: 'releases.unreachable',
  RateLimited: 'releases.rateLimited',
  Rejected: 'releases.rejected',
};

@Component({
  selector: 'app-releases-page',
  imports: [MatIcon, MarkdownPipe, LoadingErrorSectionWidget, LoadingSectionWidget],
  templateUrl: './releases.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './releases.page.scss',
})
export class ReleasesPage implements OnInit {
  /* Injections */
  private readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly appSettingsService: AppSettingsService = inject(AppSettingsService);
  private readonly releasesService: ReleasesService = inject(ReleasesService);

  /* I18n signals */
  protected readonly title: Signal<string> = computed((): string => this.i18n.t('releases.title'));
  protected readonly description: Signal<string> = computed((): string => this.i18n.t('releases.description'));
  protected readonly currentLabel: Signal<string> = computed((): string => this.i18n.t('releases.current'));
  protected readonly newerLabel: Signal<string> = computed((): string => this.i18n.t('releases.newer'));
  protected readonly prereleaseLabel: Signal<string> = computed((): string => this.i18n.t('releases.prerelease'));
  protected readonly emptyText: Signal<string> = computed((): string => this.i18n.t('releases.empty'));

  /* State signals */
  protected readonly overview: Signal<ReleaseOverview | null> = this.releasesService.overview;
  protected readonly loading: Signal<boolean> = this.releasesService.loading;
  protected readonly loadError: Signal<string | null> = this.releasesService.loadError;
  protected readonly versions: Signal<readonly ReleaseVersion[]> = this.releasesService.versions;

  /* Derived labels */
  protected readonly noDataAvailable: Signal<boolean> = computed((): boolean => this.overview() !== null && this.versions().length === 0);

  // An unreachable release host is a normal outcome, so it is named next to the last known list
  // instead of being raised as a load error.
  protected readonly statusNotice: Signal<string> = computed((): string => {
    const overview: ReleaseOverview | null = this.overview();

    if (overview === null || overview.status === 'Ok') {
      return '';
    }

    return this.i18n.t(STATUS_NOTICE_KEYS[overview.status]);
  });

  /* Lifecycle */
  ngOnInit(): void {
    void this.releasesService.loadAsync();
  }

  /* Handlers */
  protected publishedAtLabel(release: ReleaseVersion): string {
    return this.formatInstant(release.publishedAt);
  }

  protected isBusy(): boolean {
    return this.loading() && this.overview() === null;
  }

  /* Helpers */
  private formatInstant(instant: string): string {
    return formatDate(instant, 'medium', this.appSettingsService.localeId());
  }
}
