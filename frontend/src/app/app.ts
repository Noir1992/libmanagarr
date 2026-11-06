import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  OnInit,
  OnDestroy,
  Signal,
  signal,
  viewChild,
  WritableSignal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AppI18nService } from './services/app-i18n.service';
import { AuthService } from './services/auth.service';

const NARROW_VIEWPORT_QUERY: string = '(max-width: 820px)';
const SCROLL_TO_TOP_THRESHOLD: number = 300;
const NAVIGATION_OPEN_STORAGE_KEY: string = 'navigation:open';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, MatButtonModule, MatIconModule, MatMenuModule],
  templateUrl: './app.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './app.scss',
})
export class App implements OnInit, OnDestroy {
  private readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly authService: AuthService = inject(AuthService);
  private narrowMediaQuery?: MediaQueryList;
  protected readonly navigationOpen: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly narrowViewport: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly scrollToTopVisible: WritableSignal<boolean> = signal<boolean>(false);
  private readonly contentScroll: Signal<ElementRef<HTMLDivElement> | undefined> = viewChild<ElementRef<HTMLDivElement>>('contentScroll');
  protected readonly libraryLabel: Signal<string> = computed((): string => this.i18n.t('navigation.library'));
  protected readonly settingsLabel: Signal<string> = computed((): string => this.i18n.t('navigation.settings'));
  protected readonly codecStatsLabel: Signal<string> = computed((): string => this.i18n.t('navigation.codecStats'));
  protected readonly libraryTrendLabel: Signal<string> = computed((): string => this.i18n.t('navigation.libraryTrend'));
  protected readonly requestsLabel: Signal<string> = computed((): string => this.i18n.t('navigation.requests'));
  protected readonly calendarLabel: Signal<string> = computed((): string => this.i18n.t('navigation.calendar'));
  protected readonly symlinksLabel: Signal<string> = computed((): string => this.i18n.t('navigation.symlinks'));
  protected readonly logsLabel: Signal<string> = computed((): string => this.i18n.t('navigation.logs'));
  protected readonly navigationToggleLabel: Signal<string> = computed((): string => this.i18n.t('navigation.toggle'));
  protected readonly userMenuLabel: Signal<string> = computed((): string => this.i18n.t('navigation.userMenu'));
  protected readonly scrollToTopLabel: Signal<string> = computed((): string => this.i18n.t('common.scrollToTop'));
  protected readonly logoutLabel: Signal<string> = computed((): string => this.i18n.t('navigation.logout'));
  protected readonly logoutError = signal<string | null>(null);
  protected readonly logoutBusy = signal(false);
  protected readonly showShell: Signal<boolean> = computed(
    (): boolean => this.authService.state() === 'loading' || this.authService.state() === 'authenticated',
  );

  ngOnInit(): void {
    if (typeof globalThis.matchMedia !== 'function') {
      this.navigationOpen.set(true);

      return;
    }

    this.narrowMediaQuery = globalThis.matchMedia(NARROW_VIEWPORT_QUERY);
    this.applyNarrowViewport(this.narrowMediaQuery.matches);
    this.narrowMediaQuery.addEventListener('change', this.handleNarrowViewportChange);
  }

  ngOnDestroy(): void {
    this.narrowMediaQuery?.removeEventListener('change', this.handleNarrowViewportChange);
  }

  protected toggleNavigation(): void {
    this.navigationOpen.update((isOpen: boolean): boolean => !isOpen);
    if (!this.narrowViewport()) {
      globalThis.localStorage.setItem(NAVIGATION_OPEN_STORAGE_KEY, String(this.navigationOpen()));
    }
  }

  protected closeNavigation(): void {
    // A wide viewport keeps the navigation open; only the narrow drawer closes itself after navigation.
    if (this.narrowViewport()) {
      this.navigationOpen.set(false);
    }
  }

  protected onContentScroll(): void {
    const scrollHost: HTMLDivElement | undefined = this.contentScroll()?.nativeElement;

    this.scrollToTopVisible.set((scrollHost?.scrollTop ?? 0) > SCROLL_TO_TOP_THRESHOLD);
  }

  protected scrollToTop(): void {
    this.contentScroll()?.nativeElement.scrollTo({ top: 0, behavior: 'smooth' });
  }

  protected async logoutAsync(): Promise<void> {
    if (this.logoutBusy()) return;
    this.logoutBusy.set(true);
    this.logoutError.set(null);
    try {
      await this.authService.logoutAsync();
      globalThis.location.assign('/login');
    } catch {
      this.logoutError.set(this.i18n.t('auth.logoutFailed'));
    } finally {
      this.logoutBusy.set(false);
    }
  }

  private readonly handleNarrowViewportChange: (event: MediaQueryListEvent) => void = (event: MediaQueryListEvent): void => {
    this.applyNarrowViewport(event.matches);
  };

  private applyNarrowViewport(isNarrow: boolean): void {
    this.narrowViewport.set(isNarrow);
    this.navigationOpen.set(isNarrow ? false : globalThis.localStorage.getItem(NAVIGATION_OPEN_STORAGE_KEY) !== 'false');
  }
}
