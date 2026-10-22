import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { AppI18nService } from '../../../services/app-i18n.service';
import { ForgotPasswordDialogComponent } from './forgot-password-dialog.component';

describe('ForgotPasswordDialogComponent', () => {
  let dialogRefSpy: jasmine.SpyObj<MatDialogRef<ForgotPasswordDialogComponent>>;
  let fixture: ComponentFixture<ForgotPasswordDialogComponent>;
  const i18nSpy: jasmine.SpyObj<AppI18nService> = jasmine.createSpyObj<AppI18nService>('AppI18nService', ['t']);

  i18nSpy.t.and.callFake((key: string): string => key);

  beforeEach(async () => {
    dialogRefSpy = jasmine.createSpyObj<MatDialogRef<ForgotPasswordDialogComponent>>('MatDialogRef', ['close']);

    await TestBed.configureTestingModule({
      providers: [
        { provide: MatDialogRef, useValue: dialogRefSpy },
        { provide: AppI18nService, useValue: i18nSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ForgotPasswordDialogComponent);
    fixture.detectChanges();
  });

  it('should render the recovery description under the translated title', () => {
    expect(fixture.nativeElement.querySelector('.dialog-title').textContent).toContain('auth.forgotPasswordTitle');
    expect(fixture.nativeElement.querySelector('.dialog-content p').textContent).toContain('auth.forgotPasswordMessage');
  });

  it('should close the dialog when the close button is clicked', () => {
    (fixture.nativeElement.querySelector('mat-dialog-actions button') as HTMLButtonElement).click();

    expect(dialogRefSpy.close).toHaveBeenCalledTimes(1);
  });
});
