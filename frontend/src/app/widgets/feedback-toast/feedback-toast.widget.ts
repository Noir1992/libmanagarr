import { Component, inject, ChangeDetectionStrategy } from '@angular/core';
import { MAT_SNACK_BAR_DATA } from '@angular/material/snack-bar';

type FeedbackToastType = 'success' | 'error';

type FeedbackToastData = {
  message: string;
  type: FeedbackToastType;
};

@Component({
  selector: 'app-feedback-toast',
  templateUrl: './feedback-toast.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './feedback-toast.widget.scss',
})
export class FeedbackToastWidget {
  protected readonly data: FeedbackToastData = inject(MAT_SNACK_BAR_DATA) as FeedbackToastData;
}
