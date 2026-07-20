import { Component, Output, EventEmitter, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Editor } from '@tiptap/core';

@Component({
  selector: 'app-toolbar',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './toolbar.component.html',
  styleUrl: './toolbar.component.scss',
})
export class ToolbarComponent {
  @Input() editor: Editor | null = null;
  @Output() toggleBold = new EventEmitter<void>();
  @Output() toggleItalic = new EventEmitter<void>();
  @Output() toggleUnderline = new EventEmitter<void>();
  @Output() toggleStrike = new EventEmitter<void>();
  @Output() toggleCode = new EventEmitter<void>();
  @Output() toggleHeading = new EventEmitter<number>();
  @Output() toggleBulletList = new EventEmitter<void>();
  @Output() toggleOrderedList = new EventEmitter<void>();
  @Output() toggleTaskList = new EventEmitter<void>();
  @Output() toggleBlockquote = new EventEmitter<void>();
  @Output() undo = new EventEmitter<void>();
  @Output() redo = new EventEmitter<void>();

  isActive(format: string, attrs?: any): boolean {
    return this.editor?.isActive(format, attrs) ?? false;
  }

  canUndo(): boolean {
    return this.editor?.can().undo() ?? false;
  }

  canRedo(): boolean {
    return this.editor?.can().redo() ?? false;
  }
}
