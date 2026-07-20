import {
  Component,
  OnInit,
  OnDestroy,
  ViewChild,
  ElementRef,
  signal,
  AfterViewInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import Collaboration from '@tiptap/extension-collaboration';
import Underline from '@tiptap/extension-underline';
import TaskList from '@tiptap/extension-task-list';
import TaskItem from '@tiptap/extension-task-item';
import Placeholder from '@tiptap/extension-placeholder';
import * as Y from 'yjs';
import { SignalRService } from '../../core/services/signalr.service';
import { SignalRProvider } from '../../core/services/yjs-signalr.provider';
import { DocumentService } from '../../core/services/document.service';
import { AuthService } from '../../core/auth/auth.service';
import { PresenceUser, Document, DocumentRole } from '../../core/models';
import { ToolbarComponent } from './toolbar/toolbar.component';
import { PresencePanelComponent } from './presence-panel/presence-panel.component';
import { ShareDialogComponent } from './share-dialog/share-dialog.component';
import {
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
  FormGroup,
} from '@angular/forms';

@Component({
  selector: 'app-editor',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    ToolbarComponent,
    PresencePanelComponent,
    ShareDialogComponent,
  ],
  templateUrl: './editor.component.html',
  styleUrl: './editor.component.scss',
})
export class EditorComponent implements OnInit, AfterViewInit, OnDestroy {
  @ViewChild('editorElement', { static: false }) editorRef!: ElementRef<HTMLDivElement>;

  editor: Editor | null = null;
  document = signal<Document | null>(null);
  connected = signal(false);
  presenceUsers = signal<PresenceUser[]>([]);
  showShareDialog = signal(false);
  editingTitle = signal(false);
  isSaving = signal(false);
  titleForm!: FormGroup;

  private ydoc!: Y.Doc;
  private provider!: SignalRProvider;
  documentId = '';
  private subscriptions: Subscription[] = [];
  private titleSaveTimeout: ReturnType<typeof setTimeout> | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private signalr: SignalRService,
    private docService: DocumentService,
    private auth: AuthService,
    private fb: NonNullableFormBuilder,
  ) {
    this.titleForm = this.fb.group({
      title: ['', [Validators.required, Validators.minLength(1)]],
    });
  }

  ngOnInit(): void {
    this.documentId = this.route.snapshot.paramMap.get('id') ?? '';
    if (!this.documentId) {
      this.router.navigate(['/dashboard']);
      return;
    }

    // Load document metadata
    this.docService.getById(this.documentId).subscribe({
      next: (doc) => {
        this.document.set(doc);
        this.titleForm.patchValue({ title: doc.title });
      },
      error: () => {
        this.router.navigate(['/dashboard']);
      },
    });

    // Listen for connection status
    this.subscriptions.push(
      this.signalr.onConnected.subscribe((status) => this.connected.set(status)),
    );

    // Listen for presence updates
    this.subscriptions.push(
      this.signalr.onPresence.subscribe((users) => this.presenceUsers.set(users)),
    );
  }

  ngAfterViewInit(): void {
    this.initializeEditor();

    // Save on page unload (browser close, navigation away)
    window.addEventListener('beforeunload', this.handleBeforeUnload);
  }

  handleBeforeUnload = (): void => {
    // Save document state before the page unloads
    if (this.provider && this.documentId) {
      // Use sendBeacon-compatible approach: fire-and-forget save
      this.provider.save().catch(() => {});
    }
  }

  /**
   * Handle keyboard shortcuts. Called on keydown events.
   */
  onKeyDown(event: KeyboardEvent): void {
    // Ctrl+S / Cmd+S: Force save
    if ((event.ctrlKey || event.metaKey) && event.key === 's') {
      event.preventDefault();
      this.forceSave();
    }
  }

  /**
   * Force save the document state to the server.
   */
  async forceSave(): Promise<void> {
    console.log('[Editor] Force save triggered');
    this.isSaving.set(true);
    try {
      if (this.provider) {
        console.log('[Editor] Saving via provider...');
        await this.provider.save();
        console.log('[Editor] Provider save completed');
      }
      if (this.signalr && this.documentId) {
        console.log('[Editor] Saving via SignalR...');
        await this.signalr.saveDocument(this.documentId);
        console.log('[Editor] SignalR save completed');
      }
      console.log('[Editor] Save successful');
    } catch (err) {
      console.error('[Editor] Save failed:', err);
      this.isSaving.set(false);
      return;
    }
    setTimeout(() => this.isSaving.set(false), 1000);
  }

  ngOnDestroy(): void {
    if (this.titleSaveTimeout) clearTimeout(this.titleSaveTimeout);

    // Remove beforeunload listener
    window.removeEventListener('beforeunload', this.handleBeforeUnload);

    // Save document state before destroying to prevent data loss
    if (this.provider) {
      this.provider.save().catch(() => {});
    }
    if (this.signalr && this.documentId) {
      this.signalr.saveDocument(this.documentId).catch(() => {});
    }

    this.editor?.destroy();
    this.provider?.destroy();
    this.ydoc?.destroy();
    this.signalr.disconnect();
    this.subscriptions.forEach((s) => s.unsubscribe());
  }

  private async initializeEditor(): Promise<void> {
    console.log('[Editor] Initializing editor for document:', this.documentId);
    // 1. Create Yjs document
    this.ydoc = new Y.Doc();

    // 2. Create the provider IMMEDIATELY (before connecting) to capture any updates
    console.log('[Editor] Creating SignalR provider...');
    this.provider = new SignalRProvider(this.documentId, this.ydoc, this.signalr);

    // Set local user awareness
    const user = this.auth.user();
    if (user) {
      this.provider.setLocalState({
        name: user.displayName,
        color: user.avatarColor || '#7c5cff',
      });
    }

    // 3. Subscribe to initial state BEFORE connecting (race condition fix)
    let stateReceived = false;
    this.subscriptions.push(
      this.signalr.onInitialState.subscribe((state) => {
        console.log('[Editor] Initial state received:', state ? `has data (${state.byteLength} bytes)` : 'empty');
        if (state && state.byteLength > 0) {
          Y.applyUpdate(this.ydoc, state);
          console.log('[Editor] Applied initial state to Yjs doc');
        }
        if (!stateReceived) {
          stateReceived = true;
          console.log('[Editor] Initial state received, setting up editor...');
          this.setupEditor();
        }
      }),
    );

    // 4. Connect SignalR and get initial state
    try {
      console.log('[Editor] Connecting to SignalR...');
      await this.signalr.connect(this.documentId);
      console.log('[Editor] SignalR connected successfully');
    } catch (err) {
      console.error('[Editor] Failed to connect:', err);
      return;
    }

    // Timeout to setup editor even if no state received (for new documents)
    setTimeout(() => {
      if (!stateReceived) {
        console.warn('[Editor] No state received within timeout, setting up editor anyway');
        stateReceived = true;
        this.setupEditor();
      }
    }, 5000);
  }

  private setupEditor(): void {
    console.log('[Editor] Setting up Tiptap editor...');

    // 5. Initialize Tiptap with Yjs collaboration
    this.editor = new Editor({
      element: this.editorRef.nativeElement,
      extensions: [
        StarterKit.configure(),
        Collaboration.configure({
          document: this.ydoc,
        }),
        Underline,
        TaskList,
        TaskItem.configure({
          nested: true,
        }),
        Placeholder.configure({
          placeholder: 'Start typing to begin your document...',
        }),
      ],
      editorProps: {
        attributes: {
          class: 'tandem-editor-content',
        },
      },
      editable: true,
      onCreate: ({ editor }) => {
        console.log('[Editor] Tiptap editor created, editable:', editor.isEditable);
      },
      onUpdate: ({ editor }) => {
        console.log('[Editor] Tiptap content updated');
      },
    });
  }

  // ── Title editing ───────────────────────────────────

  startEditTitle(): void {
    this.editingTitle.set(true);
  }

  saveTitle(): void {
    if (this.titleForm.invalid) {
      return;
    }

    this.editingTitle.set(false);
    const newTitle = this.titleForm.value.title?.trim() || 'Untitled Document';
    this.titleForm.patchValue({ title: newTitle });

    if (this.titleSaveTimeout) clearTimeout(this.titleSaveTimeout);
    this.titleSaveTimeout = setTimeout(() => {
      this.docService.updateTitle(this.documentId, newTitle).subscribe();
    }, 500);
  }

  // ── Navigation ──────────────────────────────────────

  goBack(): void {
    this.router.navigate(['/dashboard']);
  }

  openShare(): void {
    this.showShareDialog.set(true);
  }

  closeShare(): void {
    this.showShareDialog.set(false);
  }

  getInitials(name: string): string {
    return name
      .split(' ')
      .map((w) => w[0])
      .join('')
      .toUpperCase()
      .slice(0, 2);
  }

  // ── Toolbar Actions ─────────────────────────────────────

  onToggleBold(): void {
    this.editor?.chain().focus().toggleBold().run();
  }

  onToggleItalic(): void {
    this.editor?.chain().focus().toggleItalic().run();
  }

  onToggleUnderline(): void {
    this.editor?.chain().focus().toggleUnderline().run();
  }

  onToggleStrike(): void {
    this.editor?.chain().focus().toggleStrike().run();
  }

  onToggleCode(): void {
    this.editor?.chain().focus().toggleCode().run();
  }

  onToggleHeading(level: number): void {
    this.editor?.chain().focus().toggleHeading({ level: level as any }).run();
  }

  onToggleBulletList(): void {
    this.editor?.chain().focus().toggleBulletList().run();
  }

  onToggleOrderedList(): void {
    this.editor?.chain().focus().toggleOrderedList().run();
  }

  onToggleTaskList(): void {
    this.editor?.chain().focus().toggleTaskList().run();
  }

  onToggleBlockquote(): void {
    this.editor?.chain().focus().toggleBlockquote().run();
  }

  onUndo(): void {
    this.editor?.chain().focus().undo().run();
  }

  onRedo(): void {
    this.editor?.chain().focus().redo().run();
  }

  // ── Share Dialog Handlers ─────────────────────────────

  onShare(data: { email: string; role: string }): void {
    this.docService.share(this.documentId, data.email, data.role).subscribe({
      next: (perm) => {
        const currentDoc = this.document();
        if (currentDoc) {
          this.document.set({
            ...currentDoc,
            collaborators: [
              ...(currentDoc.collaborators || []),
              {
                userId: perm.userId,
                displayName: perm.userDisplayName,
                avatarColor: '', // Will be filled by backend
                role: data.role,
              },
            ],
          });
        }
      },
    });
  }

  onRemoveShare(userId: string): void {
    this.docService.removeShare(this.documentId, userId).subscribe({
      next: () => {
        const currentDoc = this.document();
        if (currentDoc) {
          this.document.set({
            ...currentDoc,
            collaborators: currentDoc.collaborators?.filter((p) => p.userId !== userId) || [],
          });
        }
      },
    });
  }

  onUpdateShare(data: { userId: string; role: string }): void {
    this.docService.updateShare(this.documentId, data.userId, data.role).subscribe({
      next: () => {
        const currentDoc = this.document();
        if (currentDoc) {
          this.document.set({
            ...currentDoc,
            collaborators: currentDoc.collaborators?.map((p) =>
              p.userId === data.userId ? { ...p, role: data.role } : p,
            ) || [],
          });
        }
      },
    });
  }
}
