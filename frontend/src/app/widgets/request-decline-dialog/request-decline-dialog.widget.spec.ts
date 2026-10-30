import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { RequestDeclineDialogWidget } from './request-decline-dialog.widget';

describe('RequestDeclineDialogWidget', () => {
  let fixture: ComponentFixture<RequestDeclineDialogWidget>;
  let dialogRefSpy: jasmine.SpyObj<MatDialogRef<RequestDeclineDialogWidget>>;

  beforeEach(async () => {
    dialogRefSpy = jasmine.createSpyObj<MatDialogRef<RequestDeclineDialogWidget>>('MatDialogRef', ['close']);

    await TestBed.configureTestingModule({
      imports: [RequestDeclineDialogWidget],
      providers: [
        { provide: MatDialogRef, useValue: dialogRefSpy },
        { provide: MAT_DIALOG_DATA, useValue: { title: 'Sample Movie' } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RequestDeclineDialogWidget);
  });

  it('renders the dialog heading, the media title and the reason field', () => {
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('.request-dialog-title')?.textContent?.trim()).toBe('Decline request');
    expect(element.querySelector('.request-dialog-media-title')?.textContent?.trim()).toBe('Sample Movie');
    expect(element.querySelector('textarea[formControlName="reason"]')).not.toBeNull();
  });

  it('closes with the trimmed reason when the user confirms', () => {
    fixture.detectChanges();

    fixture.componentInstance['form'].controls.reason.setValue('  not now  ');
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.request-dialog-confirm')!.click();

    expect(dialogRefSpy.close).toHaveBeenCalledOnceWith('not now');
  });

  it('closes with an empty reason when the user confirms without one', () => {
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.request-dialog-confirm')!.click();

    expect(dialogRefSpy.close).toHaveBeenCalledOnceWith('');
  });

  it('closes without a result when the user cancels', () => {
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.request-dialog-cancel')!.click();

    expect(dialogRefSpy.close).toHaveBeenCalledOnceWith();
  });
});
