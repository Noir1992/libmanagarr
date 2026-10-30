import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { RequestDeleteDialogWidget } from './request-delete-dialog.widget';

describe('RequestDeleteDialogWidget', () => {
  let fixture: ComponentFixture<RequestDeleteDialogWidget>;
  let dialogRefSpy: jasmine.SpyObj<MatDialogRef<RequestDeleteDialogWidget>>;

  beforeEach(async () => {
    dialogRefSpy = jasmine.createSpyObj<MatDialogRef<RequestDeleteDialogWidget>>('MatDialogRef', ['close']);

    await TestBed.configureTestingModule({
      imports: [RequestDeleteDialogWidget],
      providers: [
        { provide: MatDialogRef, useValue: dialogRefSpy },
        { provide: MAT_DIALOG_DATA, useValue: { title: 'Sample Movie' } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RequestDeleteDialogWidget);
  });

  it('renders the dialog heading, the media title and the confirmation question', () => {
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('.request-dialog-title')?.textContent?.trim()).toBe('Delete request');
    expect(element.querySelector('.request-dialog-media-title')?.textContent?.trim()).toBe('Sample Movie');
    expect(element.querySelector('.request-dialog-confirm-text')?.textContent?.trim()).toBe('Remove this request from Seerr?');
    expect(element.querySelector('mat-dialog-content .request-dialog-confirm-text')).not.toBeNull();
    expect(element.querySelector('mat-dialog-actions .request-dialog-confirm')).not.toBeNull();
    expect(element.querySelector('mat-icon-button')).toBeNull();
  });

  it('closes with true when the user confirms', () => {
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.request-dialog-confirm')!.click();

    expect(dialogRefSpy.close).toHaveBeenCalledOnceWith(true);
  });

  it('closes without a result when the user cancels', () => {
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.request-dialog-cancel')!.click();

    expect(dialogRefSpy.close).toHaveBeenCalledOnceWith();
  });
});
