import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { SymlinkDirectoryListing } from '../../../../interfaces/symlinks/symlink-settings';
import { AppI18nService } from '../../../../services/app-i18n.service';
import { TranslationKey } from '../../../../services/i18n/_i18n-key';
import { SymlinkService } from '../../../../services/symlink.service';

@Component({
  selector: 'app-directory-browser-dialog',
  imports: [MatDialogModule, MatButtonModule, MatIconModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './directory-browser-dialog.component.html',
  styleUrl: './directory-browser-dialog.component.scss',
})
export class DirectoryBrowserDialogComponent implements OnInit {
  private readonly service = inject(SymlinkService);
  private readonly i18n = inject(AppI18nService);
  private readonly data = inject<{ path: string }>(MAT_DIALOG_DATA);
  protected readonly listing = signal<SymlinkDirectoryListing | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected t(key: TranslationKey): string {
    return this.i18n.t(key);
  }

  async ngOnInit(): Promise<void> {
    if (!(await this.load(this.data.path))) await this.load(null);
  }

  protected async load(path: string | null): Promise<boolean> {
    if (this.loading()) return false;
    this.loading.set(true);
    this.error.set(null);
    try {
      this.listing.set(await this.service.browseDirectories(path));
      return true;
    } catch (error) {
      const detail: unknown = error instanceof HttpErrorResponse ? error.error?.detail : null;
      this.error.set(typeof detail === 'string' ? detail : this.t('settings.symlinks.browser.error'));
      return false;
    } finally {
      this.loading.set(false);
    }
  }
}
