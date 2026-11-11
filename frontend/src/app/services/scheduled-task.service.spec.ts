import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ScheduledTaskSettings } from '../interfaces/settings/scheduled-task-settings';
import { AppI18nService } from './app-i18n.service';
import { ScheduledTaskService } from './scheduled-task.service';

describe('ScheduledTaskService', () => {
  let service: ScheduledTaskService;
  let http: HttpTestingController;
  const task = (taskKey = 'arrDataSync', revision = 0): ScheduledTaskSettings => ({
    taskKey,
    revision,
    enabled: true,
    readOnly: false,
    canRunManually: true,
    scheduleMode: 'Interval',
    intervalUnit: 'Hour',
    intervalValue: 1,
    timeHour: 0,
    timeMinute: 0,
    everyNDays: 1,
    lastRunAt: null,
    nextRunAt: '2026-10-22T12:00:00Z',
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: AppI18nService, useValue: { t: (key: string) => key } }],
    });
    service = TestBed.inject(ScheduledTaskService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('loads a list and displays exactly the persisted due time', async () => {
    const pending = service.load();
    expect(service.loading()).toBeTrue();
    http.expectOne('/api/settings/tasks').flush([task(), task('cleanup'), task('thirdTask')]);
    await pending;
    expect(service.tasks()).toHaveSize(3);
    expect(service.tasks()[0].nextRunAt).toEqual(new Date('2026-10-22T12:00:00Z'));
    expect(service.loading()).toBeFalse();
  });

  it('sends only configuration and revision to the selected task', async () => {
    const load = service.load();
    http.expectOne('/api/settings/tasks').flush([task(), task('thirdTask')]);
    await load;
    const pending = service.save({ ...task('thirdTask'), intervalValue: 7 });
    expect(service.states()['thirdTask'].saving).toBeTrue();
    expect(service.states()['arrDataSync']).toBeUndefined();
    const request = http.expectOne('/api/settings/tasks/thirdTask');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      revision: 0,
      enabled: true,
      scheduleMode: 'Interval',
      intervalUnit: 'Hour',
      intervalValue: 7,
      timeHour: 0,
      timeMinute: 0,
      everyNDays: 1,
    });
    request.flush({ ...task('thirdTask', 1), intervalValue: 7 });
    await pending;
    expect(service.tasks()[1].intervalValue).toBe(7);
    expect(service.tasks()[1].revision).toBe(1);
    expect(service.tasks()[0].revision).toBe(0);
    expect(service.states()['thirdTask'].saving).toBeFalse();
  });

  it('retains per-task validation errors and clears busy state', async () => {
    const pending = service.save(task()).catch((error) => error as Error);
    http
      .expectOne('/api/settings/tasks/arrDataSync')
      .flush({ errors: { intervalValue: ['Out of range'] } }, { status: 400, statusText: 'Bad Request' });
    expect((await pending)?.message).toBe('Out of range');
    expect(service.states()['arrDataSync'].error).toBe('Out of range');
    expect(service.states()['arrDataSync'].saving).toBeFalse();
  });

  it('refreshes revisions after a conflict and keeps the conflict visible', async () => {
    const pending = service.save(task()).catch((error) => error as Error);
    http.expectOne('/api/settings/tasks/arrDataSync').flush({ title: 'Reload task' }, { status: 409, statusText: 'Conflict' });
    await Promise.resolve();
    await Promise.resolve();
    http.expectOne('/api/settings/tasks').flush([task('arrDataSync', 5)]);
    expect((await pending)?.message).toBe('Reload task');
    expect(service.tasks()[0].revision).toBe(5);
    expect(service.states()['arrDataSync'].error).toBe('Reload task');
  });

  it('uses the explicit run route and retains the schedule after failure', async () => {
    const load = service.load();
    http.expectOne('/api/settings/tasks').flush([task()]);
    await load;
    const before = service.tasks();
    const pending = service.run('arrDataSync').catch((error) => error as Error);
    expect(service.states()['arrDataSync'].running).toBeTrue();
    http.expectOne('/api/settings/tasks/arrDataSync/run').flush({ title: 'Task failed' }, { status: 500, statusText: 'Error' });
    expect((await pending)?.message).toBe('Task failed');
    expect(service.states()['arrDataSync'].running).toBeFalse();
    expect(service.tasks()).toBe(before);
  });

  describe('success feedback expiry', () => {
    beforeEach(() => jasmine.clock().install());
    afterEach(() => jasmine.clock().uninstall());

    for (const operation of ['save', 'run'] as const) {
      it(`clears success feedback 2.2 seconds after ${operation}`, async () => {
        const pending = operation === 'save' ? service.save(task()) : service.run('arrDataSync');
        http.expectOne(`/api/settings/tasks/arrDataSync${operation === 'run' ? '/run' : ''}`).flush(task());
        await pending;

        expect(service.states()['arrDataSync'].success).toBeTrue();
        jasmine.clock().tick(2199);
        expect(service.states()['arrDataSync'].success).toBeTrue();
        jasmine.clock().tick(1);
        expect(service.states()['arrDataSync'].success).toBeFalse();
      });
    }

    it('expires feedback independently for each task', async () => {
      const first = service.run('arrDataSync');
      http.expectOne('/api/settings/tasks/arrDataSync/run').flush(task());
      await first;
      jasmine.clock().tick(1000);
      const second = service.run('cleanup');
      http.expectOne('/api/settings/tasks/cleanup/run').flush(task('cleanup'));
      await second;

      jasmine.clock().tick(1200);
      expect(service.states()['arrDataSync'].success).toBeFalse();
      expect(service.states()['cleanup'].success).toBeTrue();
      jasmine.clock().tick(1000);
      expect(service.states()['cleanup'].success).toBeFalse();
    });

    it('gives a subsequent success its full feedback duration', async () => {
      const first = service.run('arrDataSync');
      http.expectOne('/api/settings/tasks/arrDataSync/run').flush(task());
      await first;
      jasmine.clock().tick(1000);
      const second = service.save(task());
      http.expectOne('/api/settings/tasks/arrDataSync').flush(task());
      await second;

      jasmine.clock().tick(1200);
      expect(service.states()['arrDataSync'].success).toBeTrue();
      jasmine.clock().tick(1000);
      expect(service.states()['arrDataSync'].success).toBeFalse();
    });

    it('preserves a subsequent error after the previous success expires', async () => {
      const first = service.run('arrDataSync');
      http.expectOne('/api/settings/tasks/arrDataSync/run').flush(task());
      await first;
      jasmine.clock().tick(1000);
      const second = service.run('arrDataSync').catch((error) => error as Error);
      http.expectOne('/api/settings/tasks/arrDataSync/run').flush({ title: 'Task failed' }, { status: 500, statusText: 'Error' });
      await second;

      jasmine.clock().tick(2200);
      expect(service.states()['arrDataSync'].success).toBeFalse();
      expect(service.states()['arrDataSync'].error).toBe('Task failed');
    });
  });
});
