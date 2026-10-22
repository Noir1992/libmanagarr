import { ChangeDetectionStrategy, Component, computed, inject, Signal, signal, WritableSignal } from '@angular/core';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatInput } from '@angular/material/input';
import { Router } from '@angular/router';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { AuthRequestError, translateAuthError, translateAuthFieldError } from '../../services/auth-error.util';
import { AuthService } from '../../services/auth.service';
import { InfoIconWidget } from '../../widgets/info-icon/info-icon.widget';

type SetupFormGroup = FormGroup<{
  username: FormControl<string>;
  password: FormControl<string>;
  confirmPassword: FormControl<string>;
}>;

interface SetupFieldErrors {
  readonly username: string | null;
  readonly password: string | null;
  readonly confirmPassword: string | null;
}

const NO_FIELD_ERRORS: SetupFieldErrors = { username: null, password: null, confirmPassword: null };

/**
 * First-run account creation page. It is only reachable while the backend reports that no
 * account exists yet; a successful creation signs the user in directly and lands on the
 * library. Validation failures from the API are translated per field.
 */
@Component({
  selector: 'app-setup-page',
  imports: [ReactiveFormsModule, MatButtonModule, MatInput, InfoIconWidget],
  templateUrl: './setup.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './setup.page.scss',
})
export class SetupPage {
  /* Injections */
  private readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly authService: AuthService = inject(AuthService);
  private readonly appSettingsService: AppSettingsService = inject(AppSettingsService);
  private readonly router: Router = inject(Router);
  private readonly formBuilder: NonNullableFormBuilder = inject(NonNullableFormBuilder);

  /* I18n signals */
  protected readonly setupTitle: Signal<string> = computed((): string => this.i18n.t('auth.setupTitle'));
  protected readonly usernameLabel: Signal<string> = computed((): string => this.i18n.t('auth.username'));
  protected readonly passwordLabel: Signal<string> = computed((): string => this.i18n.t('auth.password'));
  protected readonly passwordLengthMessage: Signal<string> = computed((): string => this.i18n.t('auth.passwordLength'));
  protected readonly passwordRequirements: Signal<string> = computed((): string => this.i18n.t('auth.passwordRequirements'));
  protected readonly confirmPasswordLabel: Signal<string> = computed((): string => this.i18n.t('auth.confirmPassword'));
  protected readonly createAccountLabel: Signal<string> = computed((): string => this.i18n.t('auth.createAccount'));

  /* Signals */
  protected readonly busy: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly formError: WritableSignal<string | null> = signal<string | null>(null);
  protected readonly fieldErrors: WritableSignal<SetupFieldErrors> = signal<SetupFieldErrors>(NO_FIELD_ERRORS);

  /* Forms */
  protected readonly setupForm: SetupFormGroup = this.formBuilder.group(
    {
      username: this.formBuilder.control<string>('', [Validators.required]),
      password: this.formBuilder.control<string>('', [Validators.required, Validators.minLength(12), Validators.maxLength(128)]),
      confirmPassword: this.formBuilder.control<string>('', [Validators.required]),
    },
    {
      validators: (form: AbstractControl): ValidationErrors | null =>
        form.get('password')?.value === form.get('confirmPassword')?.value ? null : { passwordMismatch: true },
    },
  );

  /* Handlers */
  protected async submitSetupAsync(): Promise<void> {
    if (this.setupForm.invalid || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.setupForm.disable();
    this.formError.set(null);
    this.fieldErrors.set(NO_FIELD_ERRORS);

    try {
      const { username, password, confirmPassword }: { username: string; password: string; confirmPassword: string } =
        this.setupForm.getRawValue();
      await this.authService.setupAsync(username.trim(), password, confirmPassword);

      this.setupForm.reset({ username: '', password: '', confirmPassword: '' });
      await this.appSettingsService.load();
      await this.router.navigateByUrl('/library', { replaceUrl: true });
    } catch (error) {
      this.applyError(error);
    } finally {
      this.setupForm.enable();
      this.busy.set(false);
    }
  }

  /* Helpers */
  private applyError(error: unknown): void {
    if (!(error instanceof AuthRequestError)) {
      this.formError.set(this.i18n.t('auth.loadFailed'));
      return;
    }

    const fieldCodes: SetupFieldErrors = {
      username: this.firstFieldMessage(error, 'username'),
      password: this.firstFieldMessage(error, 'password'),
      confirmPassword: this.firstFieldMessage(error, 'confirmPassword'),
    };

    this.fieldErrors.set(fieldCodes);

    if (fieldCodes.username === null && fieldCodes.password === null && fieldCodes.confirmPassword === null) {
      this.formError.set(translateAuthError(error, this.i18n));
    }
  }

  private firstFieldMessage(error: AuthRequestError, field: string): string | null {
    const codes: string[] | undefined = error.fieldErrors[field];
    const firstCode: string | undefined = codes?.[0];

    return firstCode === undefined ? null : translateAuthFieldError(firstCode, this.i18n);
  }
}
