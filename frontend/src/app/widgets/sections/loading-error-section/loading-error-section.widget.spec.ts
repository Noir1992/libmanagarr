import { ComponentFixture, TestBed } from '@angular/core/testing';

import { LoadingErrorSectionWidget } from './loading-error-section.widget';

describe('LoadingErrorWidget', () => {
  let component: LoadingErrorSectionWidget;
  let fixture: ComponentFixture<LoadingErrorSectionWidget>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LoadingErrorSectionWidget],
    }).compileComponents();

    fixture = TestBed.createComponent(LoadingErrorSectionWidget);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('error', 'Oops');
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
