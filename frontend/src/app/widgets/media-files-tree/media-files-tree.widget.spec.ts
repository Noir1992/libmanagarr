import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MediaFilesTreeWidget } from './media-files-tree.widget';
import { MediaTreeNode } from '../../interfaces/media-tree/media-tree-node';

describe('MediaFilesTreeWidget', () => {
  let fixture: ComponentFixture<MediaFilesTreeWidget>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MediaFilesTreeWidget],
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
});

function createRoots(): MediaTreeNode[] {
  return [
    {
      key: 'sonarr-root',
      label: 'Sonarr (Sonarr A)',
      isDirectory: true,
      children: [],
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
