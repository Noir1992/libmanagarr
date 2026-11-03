import { AuthService } from './services/auth.service';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { computed, signal, WritableSignal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { ReleaseOverview } from './interfaces/releases/release-overview';
import { ReleaseVersion } from './interfaces/releases/release-version';
import { ReleasesService } from './services/releases.service';
import { App } from './app';
import { appRoutes } from './app.routes';

const NAVIGATION_OPEN_STORAGE_KEY: string = 'navigation:open';

describe('App', () => {
  let overview: WritableSignal<ReleaseOverview | null>;
  let releasesService: ReleasesService;

  beforeEach(async () => {
    globalThis.localStorage.removeItem(NAVIGATION_OPEN_STORAGE_KEY);
    overview = signal<ReleaseOverview | null>(null);
    releasesService = {
      overview,
      loading: signal<boolean>(false),
      loadError: signal<string | null>(null),
      versions: computed((): readonly ReleaseVersion[] => overview()?.versions ?? []),
      currentVersion: computed((): string => overview()?.currentVersion ?? ''),
      updateAvailable: computed((): boolean => overview()?.updateAvailable ?? false),
      loadAsync: async (): Promise<void> => undefined,
      loadOnceAsync: async (): Promise<void> => undefined,
    } as unknown as ReleasesService;

    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(withXhr()), provideRouter(appRoutes), { provide: ReleasesService, useValue: releasesService }],
    }).compileComponents();
  });

  it('shows the running version in the navigation and names it for screen readers', () => {
    overview.set(overviewWith('1.3.0', false));

    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const versionEntry: Element | null = fixture.nativeElement.querySelector('.navigation-version');

    expect(versionEntry?.getAttribute('href')).toContain('/releases');
    expect(versionEntry?.getAttribute('aria-label')).toBe("What's new");
    expect(versionEntry?.querySelector('.navigation-version-label')?.textContent).toContain('Version:');
    expect(versionEntry?.querySelector('.navigation-version-value')?.textContent).toContain('1.3.0');
  });

  it('raises the update icon only while a newer stable release exists', () => {
    overview.set(overviewWith('1.3.0', false));

    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const hiddenIcon: Element | null = fixture.nativeElement.querySelector('.navigation-version-icon');

    expect(hiddenIcon?.classList.contains('is-visible')).toBeFalse();

    overview.set(overviewWith('1.3.0', true));
    fixture.detectChanges();

    const visibleIcon: Element | null = fixture.nativeElement.querySelector('.navigation-version-icon');

    expect(visibleIcon?.classList.contains('is-visible')).toBeTrue();
  });

  // The frame is the entry's own identity, so the rules resolving in the browser are the proof:
  // it is always tinted, an update only reveals the icon, and the tint follows the theme.
  it('keeps the version entry framed and reveals only the update icon', () => {
    const originalTheme: string | undefined = document.documentElement.dataset['theme'];

    try {
      overview.set(overviewWith('1.3.0', false));
      document.documentElement.dataset['theme'] = 'light';
      const fixture: ComponentFixture<App> = TestBed.createComponent(App);
      fixture.detectChanges();

      const entry: HTMLElement = fixture.nativeElement.querySelector('.navigation-version');
      const icon: HTMLElement = fixture.nativeElement.querySelector('.navigation-version-icon');
      const calmBackground: string = getComputedStyle(entry).backgroundColor;
      const calmBorder: string = getComputedStyle(entry).borderTopColor;

      expect(calmBackground).not.toBe('rgba(0, 0, 0, 0)');
      expect(getComputedStyle(icon).visibility).toBe('hidden');

      overview.set(overviewWith('1.3.0', true));
      fixture.detectChanges();

      expect(getComputedStyle(icon).visibility).toBe('visible');
      expect(getComputedStyle(entry).backgroundColor).toBe(calmBackground);
      expect(getComputedStyle(entry).borderTopColor).toBe(calmBorder);

      // The frame border carries the theme tint and is not transitioned, so it reports the swap
      // immediately; the background fades in over 140ms on anchors and still reports its old
      // colour to a synchronous read, so it cannot prove the theme change here.
      document.documentElement.dataset['theme'] = 'dark';

      expect(getComputedStyle(entry).borderTopColor).not.toBe(calmBorder);
    } finally {
      if (originalTheme === undefined) delete document.documentElement.dataset['theme'];
      else document.documentElement.dataset['theme'] = originalTheme;
    }
  });

  it('shows a logout failure without claiming the session ended', async () => {
    const fixture = TestBed.createComponent(App);
    const logout = spyOn(TestBed.inject(AuthService), 'logoutAsync').and.rejectWith(new Error('network unavailable'));
    await fixture.componentInstance['logoutAsync']();
    await fixture.whenStable();
    expect(logout).toHaveBeenCalledTimes(1);
    expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent).toContain('Sign-out failed');
  });

  it('should create the app', () => {
    const fixture: ComponentFixture<App> = TestBed.createComponent(App);
    const app: App = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render the router outlet shell', async () => {
    const fixture: ComponentFixture<App> = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('router-outlet')).toBeTruthy();
  });

  it('should render the fixed navigation shell', () => {
    const fixture: ComponentFixture<App> = TestBed.createComponent(App);
    fixture.detectChanges();

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;
    const navigationItem: Element | null = compiled.querySelector('.navigation-item');
    const userMenuTrigger: Element | null = compiled.querySelector('.user-menu');

    expect(compiled.querySelector('.app-header')).toBeTruthy();
    expect(compiled.querySelector('.app-sidebar')).toBeTruthy();
    expect(compiled.querySelector('.app-title')?.textContent).toContain('LibManagarr');
    expect(navigationItem?.textContent).toContain('Library');
    expect(userMenuTrigger).toBeTruthy();
  });

  it('should open the navigation by default on a wide viewport', () => {
    stubNarrowViewport(false);
    const fixture: ComponentFixture<App> = TestBed.createComponent(App);
    fixture.detectChanges();

    expect(isNavigationOpen(fixture)).toBeTrue();
  });

  it('should keep the navigation closed by default on a narrow viewport', () => {
    stubNarrowViewport(true);
    const fixture: ComponentFixture<App> = TestBed.createComponent(App);
    fixture.detectChanges();

    expect(isNavigationOpen(fixture)).toBeFalse();
  });

  it('should collapse the navigation from the toggle on a wide viewport', () => {
    stubNarrowViewport(false);
    const fixture: ComponentFixture<App> = TestBed.createComponent(App);
    fixture.detectChanges();

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;
    const toggleButton: HTMLButtonElement | null = compiled.querySelector<HTMLButtonElement>('.navigation-toggle');

    toggleButton?.click();
    fixture.detectChanges();

    expect(isNavigationOpen(fixture)).toBeFalse();
    expect(compiled.querySelector('.app-sidebar')?.hasAttribute('inert')).toBeTrue();
  });

  it('should persist the wide navigation toggle and start closed on the next app start', () => {
    stubNarrowViewport(false);
    const fixture: ComponentFixture<App> = TestBed.createComponent(App);
    fixture.detectChanges();

    const toggleButton: HTMLButtonElement | null = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '.navigation-toggle',
    );
    toggleButton?.click();
    fixture.detectChanges();

    expect(globalThis.localStorage.getItem(NAVIGATION_OPEN_STORAGE_KEY)).toBe('false');

    const nextFixture: ComponentFixture<App> = TestBed.createComponent(App);
    nextFixture.detectChanges();

    expect(isNavigationOpen(nextFixture)).toBeFalse();
  });

  it('should not persist the navigation state on a narrow viewport', () => {
    stubNarrowViewport(true);
    const fixture: ComponentFixture<App> = TestBed.createComponent(App);
    fixture.detectChanges();

    const toggleButton: HTMLButtonElement | null = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '.navigation-toggle',
    );
    toggleButton?.click();
    fixture.detectChanges();
    expect(isNavigationOpen(fixture)).toBeTrue();
    expect(globalThis.localStorage.getItem(NAVIGATION_OPEN_STORAGE_KEY)).toBeNull();

    toggleButton?.click();
    fixture.detectChanges();
    expect(globalThis.localStorage.getItem(NAVIGATION_OPEN_STORAGE_KEY)).toBeNull();
  });

  it('should keep the navigation open after using a menu item on a wide viewport', () => {
    stubNarrowViewport(false);
    const fixture: ComponentFixture<App> = TestBed.createComponent(App);
    fixture.detectChanges();

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;

    fixture.componentInstance['closeNavigation']();
    fixture.detectChanges();

    expect(isNavigationOpen(fixture)).toBeTrue();
    expect(compiled.querySelector('.navigation-backdrop')).toBeNull();
  });

  it('should close the narrow drawer after using a menu item', () => {
    stubNarrowViewport(true);
    const fixture: ComponentFixture<App> = TestBed.createComponent(App);
    fixture.detectChanges();

    const toggleButton: HTMLButtonElement | null = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '.navigation-toggle',
    );
    toggleButton?.click();
    fixture.detectChanges();
    expect(isNavigationOpen(fixture)).toBeTrue();

    fixture.componentInstance['closeNavigation']();
    fixture.detectChanges();

    expect(isNavigationOpen(fixture)).toBeFalse();
  });

  it('should render the navigation backdrop on an open narrow drawer', () => {
    stubNarrowViewport(true);
    const fixture: ComponentFixture<App> = TestBed.createComponent(App);
    fixture.detectChanges();

    const toggleButton: HTMLButtonElement | null = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '.navigation-toggle',
    );
    toggleButton?.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.navigation-backdrop')).toBeTruthy();
  });

  it('should not render the navigation backdrop on a wide viewport', () => {
    stubNarrowViewport(false);
    const fixture: ComponentFixture<App> = TestBed.createComponent(App);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.navigation-backdrop')).toBeNull();
  });

  it('should toggle the mobile navigation state', () => {
    stubNarrowViewport(true);
    const fixture: ComponentFixture<App> = TestBed.createComponent(App);
    fixture.detectChanges();

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;
    const toggleButton: HTMLButtonElement | null = compiled.querySelector<HTMLButtonElement>('.navigation-toggle');

    toggleButton?.click();
    fixture.detectChanges();
    expect(isNavigationOpen(fixture)).toBeTrue();
    expect(compiled.querySelector('.app-sidebar')?.hasAttribute('inert')).toBeFalse();

    toggleButton?.click();
    fixture.detectChanges();
    expect(isNavigationOpen(fixture)).toBeFalse();
  });

  it('should hide the scroll-to-top button while the content is not scrolled', () => {
    const fixture: ComponentFixture<App> = TestBed.createComponent(App);
    fixture.detectChanges();

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('.scroll-to-top')).toBeNull();
  });

  it('should show the scroll-to-top button once scrolled past the threshold', () => {
    const fixture: ComponentFixture<App> = TestBed.createComponent(App);
    fixture.detectChanges();

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;
    const scrollHost: HTMLElement = makeScrollable(compiled);

    scrollHost.scrollTop = 301;
    scrollHost.dispatchEvent(new Event('scroll'));
    fixture.detectChanges();

    expect(compiled.querySelector('.scroll-to-top')).toBeTruthy();

    scrollHost.scrollTop = 0;
    scrollHost.dispatchEvent(new Event('scroll'));
    fixture.detectChanges();

    expect(compiled.querySelector('.scroll-to-top')).toBeNull();
  });

  it('should scroll the content back to the top on click', () => {
    const fixture: ComponentFixture<App> = TestBed.createComponent(App);
    fixture.detectChanges();

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;
    const scrollHost: HTMLElement = makeScrollable(compiled);
    const scrollToSpy: jasmine.Spy = spyOn(scrollHost, 'scrollTo');

    scrollHost.scrollTop = 400;
    scrollHost.dispatchEvent(new Event('scroll'));
    fixture.detectChanges();

    const scrollTopButton: HTMLButtonElement | null = compiled.querySelector<HTMLButtonElement>('.scroll-to-top');
    scrollTopButton?.click();
    fixture.detectChanges();

    expect(scrollToSpy).toHaveBeenCalledWith({ top: 0, behavior: 'smooth' });
  });

  it('should elevate the scroll-to-top button per theme', () => {
    const fixture: ComponentFixture<App> = TestBed.createComponent(App);
    fixture.detectChanges();

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;
    const scrollHost: HTMLElement = makeScrollable(compiled);
    const originalTheme: string | undefined = document.documentElement.dataset['theme'];

    scrollHost.scrollTop = 400;
    scrollHost.dispatchEvent(new Event('scroll'));
    fixture.detectChanges();

    const scrollButton: HTMLElement | null = compiled.querySelector<HTMLElement>('.scroll-to-top');

    try {
      document.documentElement.dataset['theme'] = 'light';
      const lightStyle: CSSStyleDeclaration = getComputedStyle(scrollButton as HTMLElement);
      const lightBorder: string = lightStyle.borderTopColor;

      document.documentElement.dataset['theme'] = 'dark';
      const darkStyle: CSSStyleDeclaration = getComputedStyle(scrollButton as HTMLElement);

      // Light keeps the shared border token; dark swaps to the lightened elevated variant.
      expect(lightBorder).toBe('rgba(43, 60, 74, 0.14)');
      expect(darkStyle.borderTopColor).not.toBe('rgba(196, 210, 223, 0.14)');
      expect(darkStyle.borderTopColor).not.toBe(lightBorder);

      // Both themes must render the elevation shadow.
      expect(lightStyle.boxShadow).not.toBe('none');
      expect(darkStyle.boxShadow).not.toBe('none');
    } finally {
      if (originalTheme === undefined) delete document.documentElement.dataset['theme'];
      else document.documentElement.dataset['theme'] = originalTheme;
    }
  });

  function makeScrollable(compiled: HTMLElement): HTMLElement {
    const scrollHost: HTMLElement = compiled.querySelector<HTMLElement>('.content-scroll') as HTMLElement;
    const spacer: HTMLElement = document.createElement('div');

    scrollHost.style.height = '200px';
    spacer.style.height = '2000px';
    scrollHost.appendChild(spacer);

    return scrollHost;
  }

  function stubNarrowViewport(isNarrow: boolean): void {
    const mediaQueryStub: MediaQueryList = {
      matches: isNarrow,
      addEventListener: (): void => undefined,
      removeEventListener: (): void => undefined,
    } as unknown as MediaQueryList;

    spyOn(globalThis, 'matchMedia').and.returnValue(mediaQueryStub);
  }

  function isNavigationOpen(fixture: ComponentFixture<App>): boolean {
    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;

    return compiled.querySelector('.app-frame')?.classList.contains('is-navigation-open') ?? false;
  }

  function overviewWith(currentVersion: string, updateAvailable: boolean): ReleaseOverview {
    return {
      currentVersion,
      updateAvailable,
      status: 'Ok',
      checkedAt: '2026-11-02T10:00:00.000Z',
      versions: [],
    };
  }
});
