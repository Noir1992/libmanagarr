import { Component, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MediaDetailsDialogWidget } from '../media-details-dialog/media-details-dialog.widget';
import { RadarrLibraryMovieTable } from '../../interfaces/library/radarr-library-movie-table';
import { RadarrTableType } from '../../interfaces/arr-table/radarr-table-movie-type';
import { TableColumnTemplateDirective } from '../table-column-template.directive';
import { RadarrTableWidget } from './radarr-table.widget';

@Component({
  imports: [RadarrTableWidget, TableColumnTemplateDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-radarr-table [table]="table">
      <ng-template appTableColumn [appTableColumnConfig]="{ kind: 'movie', key: 'title', header: 'Film', showExpandIcon: false }" let-row>
        <a class="text-link" [href]="getMovieUrl(row.url)">
          {{ row.title }}
        </a>
      </ng-template>
      <ng-template
        appTableColumn
        [appTableColumnConfig]="{ kind: 'movie', key: 'qualityProfileName', header: 'Profil', showExpandIcon: false }"
        let-row
      >
        {{ row.qualityProfileName }}
      </ng-template>
      <ng-template
        appTableColumn
        [appTableColumnConfig]="{
          kind: 'movie',
          key: 'upgradeUntilCustomFormatScore',
          header: 'Gewuenschter Score',
          showExpandIcon: false,
        }"
        let-row
      >
        {{ row.upgradeUntilCustomFormatScore }}
      </ng-template>
      <ng-template
        appTableColumn
        [appTableColumnConfig]="{ kind: 'movie', key: 'customFormatScore', header: 'Score', showExpandIcon: false }"
        let-row
      >
        {{ row.customFormatScore }}
      </ng-template>
    </app-radarr-table>
  `,
})
class RadarrTableHostComponent {
  readonly table: RadarrTableType = {
    instanceName: 'Radarr',
    instanceId: 'radarr-1',
    syncedAt: '2026-05-30T21:42:04.0728462Z',
    movies: [
      {
        id: 42,
        url: 'http://localhost:7878/movie/arrival-42',
        title: 'Arrival',
        titleSlug: 'arrival-42',
        qualityProfileName: 'HD',
        upgradeUntilCustomFormatScore: 1500,
        customFormatScore: 900,
        status: 'released',
        airDateUtc: null,
        monitored: true,
        hasFile: true,
        resolution: null,
        size: null,
      },
    ] as RadarrLibraryMovieTable[],
  };

  protected getMovieUrl(url: string | null): string | null {
    return url;
  }
}

describe('RadarrTableWidget', () => {
  let fixture: ComponentFixture<RadarrTableHostComponent>;
  let open: jasmine.Spy;

  beforeEach(async () => {
    open = jasmine.createSpy('open');
    await TestBed.configureTestingModule({
      imports: [RadarrTableHostComponent],
      providers: [{ provide: MatDialog, useValue: { open } }],
    }).compileComponents();

    fixture = TestBed.createComponent(RadarrTableHostComponent);
    fixture.detectChanges();
  });

  it('should render the movie row', () => {
    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;
    const cells: Array<string | undefined> = Array.from(compiled.querySelectorAll('tbody td')).map((cell: Element) =>
      cell.textContent?.trim(),
    );
    const link: HTMLAnchorElement = compiled.querySelector('.text-link') as HTMLAnchorElement;
    const row: Element | null = compiled.querySelector('tbody tr');

    expect(cells).toEqual(['Arrival', 'HD', '1500', '900']);
    expect(link.href).toBe('http://localhost:7878/movie/arrival-42');
    expect(row).toBeTruthy();
    expect(row?.classList.contains('summary-row')).toBeFalse();
  });

  it('should render a collapse button in the table header', () => {
    const collapseButton: Element | null = (fixture.nativeElement as HTMLElement).querySelector('.card-header-actions button');

    expect(collapseButton).toBeTruthy();
  });

  it('opens movie details on row click and keyboard activation while preserving title navigation', () => {
    const row = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.movie-detail-row')!;
    row.click();
    expect(open).toHaveBeenCalledOnceWith(
      MediaDetailsDialogWidget,
      jasmine.objectContaining({
        data: jasmine.objectContaining({
          title: 'Arrival',
          instanceName: 'Radarr',
          customFormatScore: 900,
          upgradeUntilCustomFormatScore: 1500,
        }),
      }),
    );
    open.calls.reset();
    const link = row.querySelector('a')!;
    const click = new MouseEvent('click', { bubbles: true, cancelable: true });
    click.preventDefault();
    link.dispatchEvent(click);
    link.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    expect(open).not.toHaveBeenCalled();
    row.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    expect(open).toHaveBeenCalledTimes(1);
  });

  it('should emit collapse toggle when clicking collapse icon', () => {
    const collapseButton: HTMLElement | null = (fixture.nativeElement as HTMLElement).querySelector('.card-header-actions button');
    const collapseIcon: HTMLElement | null = (fixture.nativeElement as HTMLElement).querySelector('.card-header-actions .collapse-icon');

    collapseButton?.click();
    fixture.detectChanges();

    expect(collapseIcon?.classList.contains('is-collapsed')).toBeTrue();
  });
});
