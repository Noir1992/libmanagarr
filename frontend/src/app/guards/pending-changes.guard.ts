import { CanDeactivateFn } from '@angular/router';
import { CanDeactivateWithPendingChanges } from '../interfaces/general/can-deactivate-with-pending-changes';

export const pendingChangesGuard: CanDeactivateFn<CanDeactivateWithPendingChanges> = (
  component: CanDeactivateWithPendingChanges,
): boolean => {
  return component.canDeactivate();
};
