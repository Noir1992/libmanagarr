import { ArrApplicationKind } from '../general/arr-application-kind';
import { PathReaderKind } from '../../enums/path-reader-kind';
import { PathReaderVerification } from '../../enums/path-reader-verification';
import { SymlinkCapabilityStatus } from '../../enums/symlink-capability-status';

/**
 * One media shelf as the backend sees it: both roots are paths of the server process, so the view only
 * ever shows them as text and never resolves them in the browser.
 */
export interface MediaStorageArea {
  id: string;
  name: string;
  moviesRoot: string;
  seriesRoot: string;
}

/**
 * Which prefix one Arr instance reports for a shelf, and which prefix the server uses for the same
 * files. The two are kept apart on purpose, so the form cannot quietly claim that equal looking
 * strings prove the same mount.
 */
export interface ArrPathMapping {
  id: string;
  instanceId: string;
  kind: ArrApplicationKind;
  instanceRoot: string;
  libManagarrRoot: string;
}

/** How a process that reads the link afterwards expects to see it; only a real check sets 'Verified'. */
export interface PathReaderView {
  id: string;
  kind: PathReaderKind;
  root: string;
  verification: PathReaderVerification;
}

/** The answer of the capability probe for one storage area, with the revision it was produced under. */
export interface SymlinkCapabilityCheck {
  storageAreaId: string;
  status: SymlinkCapabilityStatus;
  detail: string | null;
  checkedAt: string | null;
  configRevision: number;
  stale: boolean;
}

/** The configuration plus the probe answers; a mapping list without them would show an unusable setup. */
export interface SymlinkSettings {
  storageAreas: MediaStorageArea[];
  pathMappings: ArrPathMapping[];
  readerViews: PathReaderView[];
  revision: number;
  capabilityChecks: SymlinkCapabilityCheck[];
}
