import { ComponentFixture, TestBed } from '@angular/core/testing';

import { LoadingSectionWidget } from './loading-section.widget';

describe('LoadingSecion', () => {
  let component: LoadingSectionWidget;
  let fixture: ComponentFixture<LoadingSectionWidget>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LoadingSectionWidget],
    }).compileComponents();

    fixture = TestBed.createComponent(LoadingSectionWidget);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
