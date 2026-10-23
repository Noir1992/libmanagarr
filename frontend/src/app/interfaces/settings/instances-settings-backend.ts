import { ArrInstanceSettings } from './arr-instance-settings';

export interface InstancesSettingsBackend {
  sonarrInstances: ArrInstanceSettings[];
  radarrInstances: ArrInstanceSettings[];
}
