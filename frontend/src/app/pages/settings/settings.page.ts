import {
  Component,
  computed,
  HostListener,
  inject,
  OnDestroy,
  OnInit,
  Signal,
  signal,
  ViewChild,
  WritableSignal,
  ChangeDetectionStrategy,
} from '@angular/core';
import { MatTabGroup, MatTabsModule } from '@angular/material/tabs';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { CanDeactivateWithPendingChanges } from '../../interfaces/general/can-deactivate-with-pending-changes';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { SettingsAccountTabWidget } from './settings-account-tab/settings-account-tab.widget';
import { SettingsBackupTabWidget } from './settings-backup-tab/settings-backup-tab.widget';
import { SettingsGeneralTabWidget } from './settings-general-tab/settings-general-tab.widget';
import { SettingsArrInstancesTabWidget } from './settings-arr-instances-tab/settings-arr-instances-tab.widget';
import { SettingsTasksTabWidget } from './settings-tasks-tab/settings-tasks-tab.widget';

@Component({
  selector: 'app-settings-page',
  imports: [
    MatTabsModule,
    SettingsGeneralTabWidget,
    SettingsArrInstancesTabWidget,
    SettingsTasksTabWidget,
    SettingsBackupTabWidget,
    SettingsAccountTabWidget,
  ],
  templateUrl: './settings.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './settings.page.scss',
})
export class SettingsPage implements OnInit, OnDestroy, CanDeactivateWithPendingChanges {
  /* Injections */
  protected readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly activatedRoute: ActivatedRoute = inject(ActivatedRoute);
  private readonly appSettingsService: AppSettingsService = inject(AppSettingsService);
  private readonly router: Router = inject(Router);

  /* Constants */
  private static readonly tabQueryParamName: string = 'tab';
  private static readonly tabQueryParamValues: readonly string[] = ['general', 'arr-instances', 'tasks', 'backup', 'account'];

  /* View Queries */
  @ViewChild(MatTabGroup)
  private readonly settingsTabGroup?: MatTabGroup;

  @ViewChild(SettingsGeneralTabWidget)
  private readonly settingsGeneralTabWidget?: SettingsGeneralTabWidget;

  @ViewChild(SettingsArrInstancesTabWidget)
  private readonly settingsInstancesTabWidget?: SettingsArrInstancesTabWidget;

  @ViewChild(SettingsTasksTabWidget)
  private readonly settingsTasksTabWidget?: SettingsTasksTabWidget;

  @ViewChild(SettingsBackupTabWidget)
  private readonly settingsBackupTabWidget?: SettingsBackupTabWidget;

  @ViewChild(SettingsAccountTabWidget)
  private readonly settingsAccountTabWidget?: SettingsAccountTabWidget;

  /* I18n signals */
  protected readonly pageTitle: Signal<string> = computed((): string => this.i18n.t('settings.title'));
  protected readonly pageDescription: Signal<string> = computed((): string => this.i18n.t('settings.description'));
  protected readonly generalTabLabel: Signal<string> = computed((): string => this.i18n.t('settings.general'));
  protected readonly instancesTabLabel: Signal<string> = computed((): string => this.i18n.t('settings.instancesTab'));
  protected readonly tasksTabLabel: Signal<string> = computed((): string => this.i18n.t('settings.tasksTab'));
  protected readonly backupTabLabel: Signal<string> = computed((): string => this.i18n.t('settings.backupTab'));
  protected readonly accountTabLabel: Signal<string> = computed((): string => this.i18n.t('settings.accountTab'));

  /* Signals */
  protected readonly settingsLoadError: Signal<string | null> = this.appSettingsService.loadError;
  protected readonly systemTheme: WritableSignal<'light' | 'dark'> = signal<'light' | 'dark'>('light');
  protected readonly selectedTabIndex: WritableSignal<number> = signal<number>(0);

  /* State */
  private initializedPage: boolean = false;
  private mediaQuery?: MediaQueryList;
  private readonly routeSubscriptions: Subscription = new Subscription();

  /* Lifecycle */
  ngOnInit(): void {
    if (typeof globalThis.matchMedia === 'function') {
      this.mediaQuery = globalThis.matchMedia('(prefers-color-scheme: dark)');
      this.systemTheme.set(this.mediaQuery.matches ? 'dark' : 'light');
      this.mediaQuery.addEventListener('change', this.handleMediaQueryChange);
    }

    this.routeSubscriptions.add(
      this.activatedRoute.queryParamMap.subscribe((queryParamMap) => {
        const nextTabIndex: number = this.tabIndexFromQueryParam(queryParamMap.get(SettingsPage.tabQueryParamName));

        void this.handleUrlTabChangeAsync(nextTabIndex);
      }),
    );

    void this.initializePageAsync();
  }

  ngOnDestroy(): void {
    this.mediaQuery?.removeEventListener('change', this.handleMediaQueryChange);
    this.routeSubscriptions.unsubscribe();
  }

  /* Handlers */
  canDeactivate(): boolean {
    return this.canDeactivateCurrentTab();
  }

  @HostListener('window:beforeunload', ['$event'])
  protected handleBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.canDeactivateCurrentTab()) {
      return;
    }

    event.preventDefault();
  }

  protected handleTabSelectionChange(nextIndex: number): void {
    if (nextIndex === this.selectedTabIndex()) {
      return;
    }

    if (!this.canDeactivateCurrentTab()) {
      if (this.settingsTabGroup) {
        this.settingsTabGroup.selectedIndex = this.selectedTabIndex();
      }
      return;
    }

    this.selectedTabIndex.set(nextIndex);
    void this.updateTabQueryParamAsync(nextIndex, false);
  }

  private readonly handleMediaQueryChange: (event: MediaQueryListEvent) => void = (event: MediaQueryListEvent): void => {
    this.systemTheme.set(event.matches ? 'dark' : 'light');
  };

  /* Helpers */
  private async handleUrlTabChangeAsync(nextIndex: number): Promise<void> {
    if (nextIndex === this.selectedTabIndex()) {
      return;
    }

    if (!this.canDeactivateCurrentTab()) {
      await this.updateTabQueryParamAsync(this.selectedTabIndex(), true);
      return;
    }

    this.selectedTabIndex.set(nextIndex);
  }

  private async initializePageAsync(): Promise<void> {
    if (this.initializedPage) {
      return;
    }

    await this.appSettingsService.load();
    this.selectedTabIndex.set(this.readSelectedTabIndexFromUrl());
    void this.updateTabQueryParamAsync(this.selectedTabIndex(), true);

    this.initializedPage = true;
  }

  private canDeactivateCurrentTab(): boolean {
    return this.getActiveSettingsTab()?.canDeactivate() ?? true;
  }

  private getActiveSettingsTab():
    | SettingsGeneralTabWidget
    | SettingsArrInstancesTabWidget
    | SettingsTasksTabWidget
    | SettingsBackupTabWidget
    | SettingsAccountTabWidget
    | null {
    switch (this.selectedTabIndex()) {
      case 0:
        return this.settingsGeneralTabWidget ?? null;
      case 1:
        return this.settingsInstancesTabWidget ?? null;
      case 2:
        return this.settingsTasksTabWidget ?? null;
      case 3:
        return this.settingsBackupTabWidget ?? null;
      case 4:
        return this.settingsAccountTabWidget ?? null;
      default:
        return null;
    }
  }

  private readSelectedTabIndexFromUrl(): number {
    return this.tabIndexFromQueryParam(this.activatedRoute.snapshot.queryParamMap.get(SettingsPage.tabQueryParamName));
  }

  private tabIndexFromQueryParam(tabName: string | null): number {
    const normalizedTabName: string = tabName?.trim().toLowerCase() ?? '';

    switch (normalizedTabName) {
      case 'arr-instances':
        return 1;
      case 'tasks':
        return 2;
      case 'backup':
        return 3;
      case 'account':
        return 4;
      default:
        return 0;
    }
  }

  private tabQueryParamFromIndex(tabIndex: number): string {
    return SettingsPage.tabQueryParamValues[tabIndex] ?? SettingsPage.tabQueryParamValues[0];
  }

  private async updateTabQueryParamAsync(tabIndex: number, replaceUrl: boolean): Promise<void> {
    const nextTab: string = this.tabQueryParamFromIndex(tabIndex);
    const currentTab: string | null = this.activatedRoute.snapshot.queryParamMap.get(SettingsPage.tabQueryParamName);

    if (currentTab === nextTab) {
      return;
    }

    await this.router.navigate([], {
      relativeTo: this.activatedRoute,
      queryParams: {
        [SettingsPage.tabQueryParamName]: nextTab,
      },
      queryParamsHandling: 'merge',
      replaceUrl,
    });
  }
}
