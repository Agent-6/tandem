import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PresenceUser } from '../../../core/models';

@Component({
  selector: 'app-presence-panel',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './presence-panel.component.html',
  styleUrl: './presence-panel.component.scss',
})
export class PresencePanelComponent {
  @Input() users: PresenceUser[] = [];

  getInitials(name: string): string {
    return name
      .split(' ')
      .map((w) => w[0])
      .join('')
      .toUpperCase()
      .slice(0, 2);
  }
}
