import { MatSnackBar } from '@angular/material/snack-bar';
import { FeedbackToastWidget } from '../widgets/feedback-toast/feedback-toast.widget';

type FeedbackToastType = 'success' | 'error';

const DEFAULT_DURATION: number = 2500;

export function openSuccessToast(snackBar: MatSnackBar, message: string, duration: number = DEFAULT_DURATION): void {
  openFeedbackToast(snackBar, 'success', message, duration);
}

export function openErrorToast(snackBar: MatSnackBar, message: string, duration: number = DEFAULT_DURATION): void {
  openFeedbackToast(snackBar, 'error', message, duration);
}

function openFeedbackToast(snackBar: MatSnackBar, type: FeedbackToastType, message: string, duration: number = DEFAULT_DURATION): void {
  snackBar.openFromComponent(FeedbackToastWidget, {
    horizontalPosition: 'center',
    verticalPosition: 'top',
    duration,
    panelClass: [`codec-snackbar-${type}`],
    data: { type, message },
  });
}
