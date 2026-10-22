import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { NoArrInstancesConfiguredSectionWidget } from './no-arr-instances-configured-section.widget';

describe('NoArrInstancesConfiguredSectionWidget', () => {
  let component: NoArrInstancesConfiguredSectionWidget;
  let fixture: ComponentFixture<NoArrInstancesConfiguredSectionWidget>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NoArrInstancesConfiguredSectionWidget],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(NoArrInstancesConfiguredSectionWidget);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
