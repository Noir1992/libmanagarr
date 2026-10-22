import { Component, input, InputSignal, ChangeDetectionStrategy } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

type FeedbackIconState = 'loading' | 'success' | 'error' | null;
type FeedbackIconVariant = 'overlay' | 'overlay-space' | 'inline';

@Component({
  selector: 'app-feedback-icon',
  imports: [MatIconModule],
  templateUrl: './feedback-icon.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './feedback-icon.widget.scss',
})
export class FeedbackIconWidget {
  readonly state: InputSignal<FeedbackIconState> = input.required<FeedbackIconState>();
  readonly variant: InputSignal<FeedbackIconVariant> = input.required<FeedbackIconVariant>();
  readonly tooltipText: InputSignal<string | null> = input<string | null>(null);
}
