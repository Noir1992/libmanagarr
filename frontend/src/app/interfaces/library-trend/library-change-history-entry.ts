import { ArrApplicationKind } from '../general/arr-application-kind';

export interface LibraryChangeHistoryEntry {
  sourceKey: string;
  kind: ArrApplicationKind;
  instanceId: string;
  instanceName: string;
  occurredAt: string;
  changeType: 'Added' | 'Changed' | 'Deleted' | 'Updated';
  entityTitle: string;
  seriesTitle: string | null;
  filePath: string | null;
  sizeBytes: number | null;
  deltaSizeBytes: number;
}
