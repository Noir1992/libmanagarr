import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FeedbackIconWidget } from './feedback-icon.widget';

describe('FeedbackIconWidget', () => {
  let fixture: ComponentFixture<FeedbackIconWidget>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FeedbackIconWidget],
    }).compileComponents();
  });

  it('should render a loading spinner in dropdown mode', () => {
    fixture = TestBed.createComponent(FeedbackIconWidget);
    fixture.componentRef.setInput('state', 'loading');
    fixture.componentRef.setInput('variant', 'overlay');
    fixture.detectChanges();

    const icon: HTMLElement | null = fixture.nativeElement.querySelector('mat-icon.spinning');
    expect(icon).not.toBeNull();
  });

  it('should render a success icon in button mode', () => {
    fixture = TestBed.createComponent(FeedbackIconWidget);
    fixture.componentRef.setInput('state', 'success');
    fixture.componentRef.setInput('variant', 'inline');
    fixture.detectChanges();

    const icon: HTMLElement | null = fixture.nativeElement.querySelector('mat-icon');
    expect(icon?.textContent?.trim()).toBe('check');
    expect(icon?.classList.contains('success')).toBeTrue();
  });

  it('should render an error icon with title', () => {
    fixture = TestBed.createComponent(FeedbackIconWidget);
    fixture.componentRef.setInput('state', 'error');
    fixture.componentRef.setInput('variant', 'inline');
    fixture.componentRef.setInput('tooltipText', 'Oops');
    fixture.detectChanges();

    const icon: HTMLElement | null = fixture.nativeElement.querySelector('mat-icon');
    expect(icon?.textContent?.trim()).toBe('warning');
    expect(icon?.getAttribute('title')).toBe('Oops');
  });
});
