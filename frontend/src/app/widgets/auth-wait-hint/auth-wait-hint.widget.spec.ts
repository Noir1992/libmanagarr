import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { AuthRequestError } from '../../services/auth-error.util';
import { AuthWaitService } from '../../services/auth-wait.service';
import { AuthWaitHintWidget } from './auth-wait-hint.widget';

describe('AuthWaitHintWidget', () => {
  let fixture: ComponentFixture<AuthWaitHintWidget>;
  let waitService: AuthWaitService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AuthWaitHintWidget],
      providers: [{ provide: AppSettingsService, useValue: { localeId: (): string => 'en-US' } }],
    }).compileComponents();

    TestBed.inject(AppI18nService).setLanguage('en');
    waitService = TestBed.inject(AuthWaitService);
    fixture = TestBed.createComponent(AuthWaitHintWidget);
  });

  afterEach((): void => {
    waitService.clear();
  });

  function refusal(code: string, seconds: number): AuthRequestError {
    return new AuthRequestError('refused', 429, code, {}, code === 'account_locked' ? 'password' : null, 0, Date.now() + seconds * 1000);
  }

  function render(): void {
    fixture.detectChanges();
  }

  function lines(): NodeListOf<Element> {
    return fixture.nativeElement.querySelectorAll('.auth-wait-line');
  }

  it('should stay out of the way while no wait is known', () => {
    render();

    expect(lines().length).toBe(0);
  });

  it('should name the remaining time and the instant the lock runs to', () => {
    waitService.record(refusal('account_locked', 300));

    render();

    const countdown: Element | null = fixture.nativeElement.querySelector('.auth-wait-countdown');
    const detail: Element | null = fixture.nativeElement.querySelector('.auth-wait-detail');

    expect(countdown?.textContent).toContain('Try again in 05:00.');
    expect(detail?.textContent).toContain('Unlocks at');
  });

  it('should keep the ticking countdown out of the accessibility tree', () => {
    waitService.record(refusal('account_locked', 300));

    render();

    expect(fixture.nativeElement.querySelector('.auth-wait-countdown')?.getAttribute('aria-hidden')).toBe('true');
    expect(fixture.nativeElement.querySelector('.auth-wait-detail')?.hasAttribute('aria-hidden')).toBeFalse();
  });

  it('should explain that the ip limit does not care about the proof', () => {
    waitService.record(refusal('too_many_requests', 45));

    render();

    const detail: Element | null = fixture.nativeElement.querySelector('.auth-wait-detail');

    expect(detail?.textContent).toContain('This applies whether or not the password was correct.');
    expect(lines().length).toBe(1);
  });

  it('should show one line per waiting kind when both are known', () => {
    waitService.record(refusal('account_locked', 300));
    waitService.record(refusal('too_many_requests', 45));

    render();

    expect(lines().length).toBe(2);
  });

  it('should disappear once the recorded deadline has passed', () => {
    waitService.record(refusal('account_locked', 300));
    render();
    expect(lines().length).toBe(1);

    waitService.record(refusal('account_locked', -1));
    render();

    expect(lines().length).toBe(0);
  });
});
