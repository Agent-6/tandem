import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CollaboratorInfo } from '../../../core/models';
import {
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
  FormGroup,
} from '@angular/forms';

@Component({
  selector: 'app-share-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule],
  templateUrl: './share-dialog.component.html',
  styleUrl: './share-dialog.component.scss',
})
export class ShareDialogComponent {
  @Input() isOpen = false;
  @Input() documentId = '';
  @Input() permissions: CollaboratorInfo[] = [];
  @Output() close = new EventEmitter<void>();
  @Output() share = new EventEmitter<{ email: string; role: string }>();
  @Output() remove = new EventEmitter<string>();
  @Output() updateRole = new EventEmitter<{ userId: string; role: string }>();
  shareForm!: FormGroup;

  constructor(private fb: NonNullableFormBuilder) {
    this.shareForm = this.fb.group({
      email: ['', [Validators.required, Validators.email]],
      role: ['Editor'],
    });
  }

  onShare(): void {
    if (this.shareForm.invalid) {
      return;
    }

    const { email, role } = this.shareForm.value;
    this.share.emit({ email: email?.trim() || '', role: role || 'Editor' });
    this.shareForm.patchValue({ email: '' });
  }

  onClose(): void {
    this.close.emit();
  }

  onRemove(userId: string): void {
    this.remove.emit(userId);
  }

  onUpdateRole(userId: string, role: string): void {
    this.updateRole.emit({ userId, role });
  }

  getRoleLabel(role: string): string {
    return role;
  }
}
