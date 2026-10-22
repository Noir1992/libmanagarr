import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DateRangeTimePickerWidget } from './date-range-time-picker.widget';

describe('DateRangeTimePickerWidget', () => {
  let fixture: ComponentFixture<DateRangeTimePickerWidget>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DateRangeTimePickerWidget],
    }).compileComponents();

    fixture = TestBed.createComponent(DateRangeTimePickerWidget);
    fixture.componentRef.setInput('label', 'Date range');
    fixture.componentRef.setInput('startDate', new Date('2026-05-26T00:00:00'));
    fixture.componentRef.setInput('endDate', new Date('2026-05-27T23:59:00'));
    fixture.detectChanges();
  });

  it('should emit a new start date when the start date changes', () => {
    const emitSpy: jasmine.Spy = spyOn(fixture.componentInstance.rangeChange, 'emit');

    fixture.componentInstance['onDateChanged']('start', new Date('2026-05-25T00:00:00'));

    expect(emitSpy).toHaveBeenCalledWith(
      jasmine.objectContaining({
        startDate: jasmine.any(Date),
        endDate: new Date('2026-05-27T23:59:00'),
      }),
    );

    const emitted = emitSpy.calls.first().args[0];
    expect(emitted.startDate.getFullYear()).toBe(2026);
    expect(emitted.startDate.getMonth()).toBe(4);
    expect(emitted.startDate.getDate()).toBe(25);
  });

  it('should emit a new end date when the end date changes', () => {
    const emitSpy: jasmine.Spy = spyOn(fixture.componentInstance.rangeChange, 'emit');

    fixture.componentInstance['onDateChanged']('end', new Date('2026-06-01T00:00:00'));

    expect(emitSpy).toHaveBeenCalledWith(
      jasmine.objectContaining({
        startDate: new Date('2026-05-26T00:00:00'),
        endDate: jasmine.any(Date),
      }),
    );

    const emitted = emitSpy.calls.first().args[0];
    expect(emitted.endDate.getFullYear()).toBe(2026);
    expect(emitted.endDate.getMonth()).toBe(5);
    expect(emitted.endDate.getDate()).toBe(1);
  });

  it('should emit a new start date with merged time when the start time changes', () => {
    const emitSpy: jasmine.Spy = spyOn(fixture.componentInstance.rangeChange, 'emit');

    const nextTime: Date = new Date();
    nextTime.setHours(8, 30, 0, 0);
    fixture.componentInstance['onTimeChanged']('start', nextTime);

    expect(emitSpy).toHaveBeenCalled();

    const emitted = emitSpy.calls.first().args[0];
    expect(emitted.startDate.getFullYear()).toBe(2026);
    expect(emitted.startDate.getMonth()).toBe(4);
    expect(emitted.startDate.getDate()).toBe(26);
    expect(emitted.startDate.getHours()).toBe(8);
    expect(emitted.startDate.getMinutes()).toBe(30);
  });

  it('should emit a new end date with merged time when the end time changes', () => {
    const emitSpy: jasmine.Spy = spyOn(fixture.componentInstance.rangeChange, 'emit');

    const nextTime: Date = new Date();
    nextTime.setHours(18, 0, 0, 0);
    fixture.componentInstance['onTimeChanged']('end', nextTime);

    expect(emitSpy).toHaveBeenCalled();

    const emitted = emitSpy.calls.first().args[0];
    expect(emitted.endDate.getFullYear()).toBe(2026);
    expect(emitted.endDate.getMonth()).toBe(4);
    expect(emitted.endDate.getDate()).toBe(27);
    expect(emitted.endDate.getHours()).toBe(18);
    expect(emitted.endDate.getMinutes()).toBe(0);
  });

  it('does not emit unchanged values from the date and time inputs', () => {
    const emitSpy = spyOn(fixture.componentInstance.rangeChange, 'emit');
    fixture.componentInstance['onDateChanged']('start', new Date('2026-05-26T00:00:00'));
    const time = new Date();
    time.setHours(23, 59, 0, 0);
    fixture.componentInstance['onTimeChanged']('end', time);
    expect(emitSpy).not.toHaveBeenCalled();
  });

  it('should not emit when the date is null', () => {
    const emitSpy: jasmine.Spy = spyOn(fixture.componentInstance.rangeChange, 'emit');

    fixture.componentInstance['onDateChanged']('start', null);

    expect(emitSpy).not.toHaveBeenCalled();
  });
});
