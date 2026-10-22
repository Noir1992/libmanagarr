import { ComponentFixture, TestBed } from '@angular/core/testing';

import { NoDataSectionWidget } from './no-data-section.widget';

describe('NoDataAvailableSectionWidget', () => {
  let component: NoDataSectionWidget;
  let fixture: ComponentFixture<NoDataSectionWidget>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NoDataSectionWidget],
    }).compileComponents();

    fixture = TestBed.createComponent(NoDataSectionWidget);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('isFiltered', false);
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
