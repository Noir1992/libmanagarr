import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { InfoIconWidget } from './info-icon.widget';

describe('InfoIconWidget', () => {
  let fixture: ComponentFixture<InfoIconWidget>;
  let overlayContainer: OverlayContainer;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [InfoIconWidget],
    }).compileComponents();

    fixture = TestBed.createComponent(InfoIconWidget);
    overlayContainer = TestBed.inject(OverlayContainer);
    fixture.componentRef.setInput('messageText', 'Log path info message');
    fixture.detectChanges();
  });

  afterEach(() => {
    overlayContainer.ngOnDestroy();
  });

  it('renders a small info trigger', () => {
    const button: HTMLButtonElement | null = fixture.nativeElement.querySelector('button');

    expect(button).not.toBeNull();
    expect(button?.textContent?.trim()).toBe('info');
  });

  it('places the popover below the icon when neither side fits', () => {
    const component = fixture.componentInstance;

    const layout = component['computeLayout'](
      { left: 20, right: 80, top: 100, bottom: 140 },
      { width: 280, height: 60 },
      { width: 300, height: 800 },
    );

    expect(layout.placement).toBe('below');
    expect(layout.panelX).toBe(5);
    expect(layout.panelY).toBe(152);
    expect(layout.arrowOffset).toBe(45);
  });

  it('places the popover above the icon when neither side fits and there is more room above', () => {
    const component = fixture.componentInstance;

    const layout = component['computeLayout'](
      { left: 20, right: 80, top: 600, bottom: 640 },
      { width: 280, height: 60 },
      { width: 300, height: 800 },
    );

    expect(layout.placement).toBe('above');
    expect(layout.panelY).toBe(528);
    expect(layout.arrowOffset).toBe(45);
  });

  it('keeps the panel five pixels from the viewport edge and tracks the arrow along the panel', () => {
    const component = fixture.componentInstance;

    const layout = component['computeLayout'](
      { left: 400, right: 430, top: 8, bottom: 34 },
      { width: 280, height: 60 },
      { width: 1000, height: 600 },
    );

    expect(layout.placement).toBe('right');
    expect(layout.panelX).toBe(442);
    expect(layout.panelY).toBe(5);
    expect(layout.arrowOffset).toBe(16);
  });

  it('opens the popover right of the trigger with an arrow pointing at the icon', async () => {
    const button: HTMLButtonElement | null = fixture.nativeElement.querySelector('button');
    button?.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const panel: Element | null = overlayContainer.getContainerElement().querySelector('.info-popover');
    expect(panel).not.toBeNull();
    expect(panel?.textContent).toContain('Log path info message');
    expect(panel?.classList).toContain('info-popover--right');
    const buttonRect = (button as HTMLButtonElement).getBoundingClientRect();
    const panelRect = (panel as Element).getBoundingClientRect();
    expect(panelRect.left).toBeGreaterThanOrEqual(buttonRect.right);

    const arrow = panel?.querySelector('.info-popover-arrow') as HTMLElement;
    expect(arrow).not.toBeNull();
    expect(arrow.style.top).not.toBe('');
    const arrowRect = arrow.getBoundingClientRect();
    const arrowCenterY = arrowRect.top + arrowRect.height / 2;
    const buttonCenterY = buttonRect.top + buttonRect.height / 2;
    expect(Math.abs(arrowCenterY - buttonCenterY)).toBeLessThanOrEqual(2);
  });

  it('closes the popover on backdrop click', async () => {
    const button: HTMLButtonElement | null = fixture.nativeElement.querySelector('button');
    button?.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const backdrop: Element | null = overlayContainer.getContainerElement().querySelector('.cdk-overlay-backdrop');
    expect(backdrop).not.toBeNull();
    backdrop?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(overlayContainer.getContainerElement().querySelector('.info-popover')).toBeNull();
  });
});
