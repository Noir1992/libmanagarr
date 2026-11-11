import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MediaFilesTreeWidget } from './media-files-tree.widget';
import { MediaTreeNode } from '../../interfaces/media-tree/media-tree-node';
import { MediaDetailsDialogWidget } from '../media-details-dialog/media-details-dialog.widget';

describe('MediaFilesTreeWidget', () => {
  let fixture: ComponentFixture<MediaFilesTreeWidget>;
  let open: jasmine.Spy;

  beforeEach(async () => {
    open = jasmine.createSpy('open');
    await TestBed.configureTestingModule({
      imports: [MediaFilesTreeWidget],
      providers: [{ provide: MatDialog, useValue: { open } }],
    }).compileComponents();

    fixture = TestBed.createComponent(MediaFilesTreeWidget);
    fixture.componentRef.setInput('scopeKey', 'languages');
    fixture.componentRef.setInput('roots', createRoots());
    fixture.detectChanges();
  });

  it('should render Radarr roots before Sonarr roots', () => {
    const rootNames: string[] = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.root-toggle .tree-name')).map(
      (element: Element) => element.textContent?.trim() ?? '',
    );

    expect(rootNames).toEqual(['Radarr (Radarr A)', 'Sonarr (Sonarr A)']);
  });

  it('should open the media details dialog with mapped data when a file row is clicked', () => {
    const interactionSpy: jasmine.Spy = jasmine.createSpy('interaction');
    fixture.componentInstance.interaction.subscribe(interactionSpy);
    sonarrRootToggle(fixture).click();
    fixture.detectChanges();
    (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('button.file-row')!.click();

    expect(open).toHaveBeenCalledOnceWith(
      MediaDetailsDialogWidget,
      jasmine.objectContaining({
        width: '700px',
        maxWidth: '95vw',
        data: jasmine.objectContaining({
          title: 'Series - S01E01 - Episode',
          url: 'http://localhost:8989/series/series-1',
          instanceName: 'Sonarr A',
          seasonNumber: 1,
          episodeNumber: 1,
          hasFile: true,
          customFormatScore: 800,
          mediaDetails: jasmine.objectContaining({ videoCodec: 'HEVC', resolution: '1080p' }),
        }),
      }),
    );
    expect(interactionSpy).toHaveBeenCalled();
  });

  it('should not open a dialog and keep rows expandable when a directory row is clicked', () => {
    sonarrRootToggle(fixture).click();
    fixture.detectChanges();

    expect(open).not.toHaveBeenCalled();
    expect((fixture.nativeElement as HTMLElement).querySelector('button.file-row')).toBeTruthy();
    sonarrRootToggle(fixture).click();
    fixture.detectChanges();

    expect(open).not.toHaveBeenCalled();
    expect((fixture.nativeElement as HTMLElement).querySelector('button.file-row')).toBeNull();
  });
});

function sonarrRootToggle(fixture: ComponentFixture<MediaFilesTreeWidget>): HTMLElement {
  const toggles: HTMLElement[] = Array.from(
    (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.root-toggle'),
  );
  const sonarr: HTMLElement | undefined = toggles.find((toggle: HTMLElement) => toggle.textContent?.includes('Sonarr'));
  return sonarr!;
}

function createRoots(): MediaTreeNode[] {
  return [
    {
      key: 'sonarr-root',
      label: 'Sonarr (Sonarr A)',
      isDirectory: true,
      children: [
        {
          key: 'sonarr-root/series-s01e01.mkv',
          label: 'series-s01e01.mkv',
          isDirectory: false,
          children: [],
          fileCount: 1,
          totalSizeBytes: 100,
          source: 'Sonarr (Sonarr A)',
          instanceName: 'Sonarr A',
          url: 'http://localhost:8989/series/series-1',
          file: {
            title: 'Series - S01E01 - Episode',
            source: 'Sonarr (Sonarr A)',
            instanceName: 'Sonarr A',
            titleSlug: 'series-1',
            sizeBytes: 100,
            relativePath: 'series/series-s01e01.mkv',
            rootTitle: 'Series',
            seasonNumber: 1,
            episodeNumber: 1,
            monitored: true,
            customFormatScore: 800,
            mediaDetails: { videoCodec: 'HEVC', resolution: '1080p' },
          },
        },
      ],
      fileCount: 1,
      totalSizeBytes: 100,
      source: 'Sonarr (Sonarr A)',
      instanceName: 'Sonarr A',
    },
    {
      key: 'radarr-root',
      label: 'Radarr (Radarr A)',
      isDirectory: true,
      children: [],
      fileCount: 1,
      totalSizeBytes: 200,
      source: 'Radarr (Radarr A)',
      instanceName: 'Radarr A',
    },
  ];
}
