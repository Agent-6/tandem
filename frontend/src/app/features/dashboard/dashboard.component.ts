import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { DocumentService } from '../../core/services/document.service';
import { Document } from '../../core/models';
import {
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
  FormGroup,
} from '@angular/forms';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent implements OnInit {
  documents = signal<Document[]>([]);
  loading = signal(true);
  showCreateDialog = signal(false);
  creating = signal(false);
  deleteTarget = signal<Document | null>(null);
  createForm!: FormGroup;

  constructor(
    public auth: AuthService,
    private docService: DocumentService,
    private router: Router,
    private fb: NonNullableFormBuilder,
  ) {
    this.createForm = this.fb.group({
      title: ['', [Validators.required, Validators.minLength(1)]],
    });
  }

  ngOnInit(): void {
    this.loadDocuments();
  }

  loadDocuments(): void {
    this.loading.set(true);
    this.docService.getAll().subscribe({
      next: (docs) => {
        this.documents.set(docs);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
      },
    });
  }

  openCreate(): void {
    this.createForm.reset();
    this.showCreateDialog.set(true);
  }

  closeCreate(): void {
    this.showCreateDialog.set(false);
  }

  createDocument(): void {
    if (this.createForm.invalid) {
      return;
    }

    const title = this.createForm.value.title?.trim() || 'Untitled Document';
    this.creating.set(true);
    this.docService.create({ title }).subscribe({
      next: (doc) => {
        this.creating.set(false);
        this.showCreateDialog.set(false);
        this.router.navigate(['/editor', doc.id]);
      },
      error: () => {
        this.creating.set(false);
      },
    });
  }

  openDocument(doc: Document): void {
    this.router.navigate(['/editor', doc.id]);
  }

  confirmDelete(doc: Document, event: Event): void {
    event.stopPropagation();
    this.deleteTarget.set(doc);
  }

  cancelDelete(): void {
    this.deleteTarget.set(null);
  }

  deleteDocument(): void {
    const doc = this.deleteTarget();
    if (!doc) return;
    this.docService.delete(doc.id).subscribe({
      next: () => {
        this.deleteTarget.set(null);
        this.loadDocuments();
      },
      error: () => {
        this.deleteTarget.set(null);
      },
    });
  }

  getInitials(name: string): string {
    return name
      .split(' ')
      .map((w) => w[0])
      .join('')
      .toUpperCase()
      .slice(0, 2);
  }

  getTimeAgo(dateStr: string): string {
    const date = new Date(dateStr);
    const now = new Date();
    const diff = now.getTime() - date.getTime();
    const minutes = Math.floor(diff / 60000);
    if (minutes < 1) return 'Just now';
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours}h ago`;
    const days = Math.floor(hours / 24);
    if (days < 7) return `${days}d ago`;
    return date.toLocaleDateString();
  }
}
