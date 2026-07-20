# Tandem — Real-Time Collaborative Document Editor

## Context

Build a Google Docs–style collaborative editor using **ASP.NET 9, Aspire, Angular, PostgreSQL, SignalR, and Yjs**. Multiple users edit the same document simultaneously with live cursors, named presence indicators, and zero lost keystrokes — even when typing at the exact same spot.

**Why this project matters:** Collaborative editing forces you to solve concurrent state conflicts — exactly the kind of "what happens when two things happen at once" thinking interviewers screen for at mid-to-senior level. The demo is genuinely impressive: open two browser tabs, type in both, watch them sync.

## Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│                        Angular SPA (Port 4200)                   │
│                                                                  │
│  ┌─────────────┐  ┌─────────────┐  ┌────────────────────────┐  │
│  │ Tiptap      │  │ Yjs Doc     │  │ Custom SignalR         │  │
│  │ Editor      │◄─┤ (CRDT)      │  │ Provider               │  │
│  │ (rich text) │  │             │  │ ┌──────────────────┐   │  │
│  └─────────────┘  └──────┬──────┘  │ │ Send updates     │   │  │
│                           │         │ │ Receive updates  │   │  │
│                           ▼         │ │ Send awareness   │   │  │
│  ┌─────────────┐  ┌─────────────┐  │ │ Receive awareness│   │  │
│  │ Presence    │  │ Document    │  │ └────────┬─────────┘   │  │
│  │ Manager     │  │ Store       │  │          │             │  │
│  │ (cursors,   │  │ (CRDT      │  └──────────┼─────────────┘  │
│  │  avatars,   │  │  state)     │             ▼             │  │
│  │  names)     │  └─────────────┘  ┌──────────────────┐   │  │
│  └─────────────┘                   │ SignalR Hub      │   │  │
│                                    │ (Collaboration)  │   │  │
└────────────────────────────────────┴────────┬─────────┘   │  │
                                               │             │  │
┌──────────────────────────────────────────────┼─────────────┼──┤
│         ASP.NET Core API (Port 5000)         │             │  │
│                                              │             │  │
│  ┌──────────────┐  ┌──────────────┐  ┌──────┴──────┐     │  │
│  │ Auth         │  │ Documents    │  │ Awareness   │     │  │
│  │ Service      │  │ CRUD API     │  │ Relay       │     │  │
│  │ (JWT)        │  │              │  │ (presence   │     │  │
│  └──────┬───────┘  └──────┬───────┘  │  cursors)   │     │  │
│         │                 │           └─────────────┘     │  │
│         ▼                 ▼                               │  │
│  ┌──────────────────────────────────┐     ┌─────────────┴───┐ │
│  │  Aspire Orchestration            │     │  SignalR        │ │
│  │  - PostgreSQL (container)        │     │  Backplane      │ │
│  │  - Redis (container)             │     └─────────────────┘ │
│  └──────────────────────────────────┘                         │
│                                                              │
└──────────────────────────────────────────────────────────────┘
                                               │
                                               ▼
                                    ┌──────────────────┐
                                    │  PostgreSQL      │
                                    │  (Postgres 16)   │
                                    │                  │
                                    │  - documents     │
                                    │  - snapshots     │
                                    │  - users         │
                                    │  - permissions   │
                                    │  - versions      │
                                    └──────────────────┘
```

### Key Design Decisions

1. **Yjs as CRDT** — Transport-agnostic. We build a **custom SignalR provider** (not y-websocket) so the Angular client sends Yjs binary updates through SignalR to the ASP.NET hub, which broadcasts to all connected clients in the same document room.
2. **No server-side CRDT merge** — Yjs handles CRDT convergence on the client. The server is a dumb relay for updates + awareness data. This avoids needing `ydotnet` or `yrs`.
3. **Debounced snapshot persistence** — Document state is saved to PostgreSQL every 5 seconds (debounced) via a timer, not on every keystroke. Full snapshots (not diff-based) stored as JSONB.
4. **JWT auth** — Access token (30 min) + refresh token (7 days) with sliding expiration. Per-document role-based permissions (viewer/editor/owner).
5. **SignalR awareness** — Yjs Awareness protocol extended over SignalR for cursor positions, colors, and user names.
6. **Aspire** — Used for local development orchestration: PostgreSQL, Redis, API, Angular dev server.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend | ASP.NET 11, Minimal APIs, EF Core 11, SignalR |
| Frontend | Angular 22, TypeScript 5.x |
| Real-time | SignalR (transport), Yjs (CRDT) |
| Database | PostgreSQL 16 (Postgres 16 via Docker) |
| Cache/Presence | Redis 7 (via Docker) |
| Auth | JWT + Refresh Tokens |
| Editor | Tiptap (ProseMirror-based) |
| CRDT | Yjs + y-text extension |
| Orchestration | .NET Aspire |

## Files to Create

### Backend (`backend/`)

```
backend/
├── Tandem.AppHost/                    # Aspire AppHost project
│   ├── Program.cs                     # Aspire app orchestration
│   ├── Tandem.AppHost.csproj
│   └── appsettings.json
├── Tandem.ServiceDefaults/            # Shared service defaults (health, telemetry)
│   ├── Program.cs
│   └── Tandem.ServiceDefaults.csproj
├── Tandem.API/                        # ASP.NET Core API project
│   ├── Program.cs                     # App startup, DI, middleware
│   ├── Tandem.API.csproj
│   ├── Controllers/
│   │   ├── AuthController.cs          # Login, register, refresh token
│   │   ├── DocumentsController.cs     # CRUD for documents
│   │   └── PermissionsController.cs # Share, remove, list permissions
│   ├── Hubs/
│   │   └── CollaborationHub.cs        # SignalR hub for Yjs sync + awareness
│   ├── Services/
│   │   ├── AuthService.cs             # JWT token generation/validation
│   │   ├── DocumentService.cs         # Document CRUD + snapshot logic
│   │   ├── SnapshotService.cs         # Debounced snapshot persistence
│   │   └── PermissionService.cs       # Document sharing/permissions
│   ├── Models/
│   │   ├── Auth/
│   │   │   ├── LoginRequest.cs
│   │   │   ├── RegisterRequest.cs
│   │   │   └── TokenResponse.cs
│   │   ├── Documents/
│   │   │   ├── DocumentDto.cs
│   │   │   ├── CreateDocumentRequest.cs
│   │   │   └── UpdateDocumentRequest.cs
│   │   ├── Permissions/
│   │   │   ├── ShareDocumentRequest.cs
│   │   │   └── DocumentPermissionDto.cs
│   │   └── ErrorDto.cs
│   ├── Entities/
│   │   ├── User.cs
│   │   ├── Document.cs
│   │   ├── DocumentSnapshot.cs
│   │   ├── DocumentVersion.cs
│   │   ├── DocumentPermission.cs
│   │   └── RefreshToken.cs
│   ├── Data/
│   │   ├── AppDbContext.cs            # EF Core DbContext
│   │   └── Migrations/                # EF migrations
│   ├── DTOs/
│   │   └── Awareness/
│   │       ├── AwarenessState.cs
│   │       └── CursorPosition.cs
│   ├── Middleware/
│   │   └── ErrorHandlerMiddleware.cs
│   └── Extensions/
│       ├── AuthExtensions.cs          # JWT config
│       ├── SignalRExtensions.cs       # SignalR config
│       └── DbExtensions.cs            # DbContext config
└── Tandem.Tests/                      # Unit/integration tests
    ├── AuthTests.cs
    ├── DocumentTests.cs
    └── CollaborationTests.cs
```

### Frontend (`frontend/`)

```
frontend/
├── angular.json
├── package.json
├── tsconfig.json
├── src/
│   ├── index.html
│   ├── main.ts
│   ├── styles.css
│   ├── app/
│   │   ├── app.config.ts              # Angular config (providers)
│   │   ├── app.routes.ts              # Route definitions
│   │   ├── app.component.ts           # Root component
│   │   ├── core/
│   │   │   ├── services/
│   │   │   │   ├── api.service.ts     # HTTP client wrapper
│   │   │   │   ├── auth.service.ts    # Auth state, token management
│   │   │   │   └── signalr.service.ts # SignalR connection management
│   │   │   ├── interceptors/
│   │   │   │   ├── auth.interceptor.ts # Add JWT to requests
│   │   │   │   └── error.interceptor.ts
│   │   │   ├── models/
│   │   │   │   ├── document.model.ts
│   │   │   │   ├── user.model.ts
│   │   │   │   └── permission.model.ts
│   │   │   └── guards/
│   │   │       ├── auth.guard.ts
│   │   │       └── permission.guard.ts
│   │   ├── features/
│   │   │   ├── auth/
│   │   │   │   ├── login/
│   │   │   │   │   ├── login.component.ts
│   │   │   │   │   └── login.component.html
│   │   │   │   └── register/
│   │   │   │       ├── register.component.ts
│   │   │   │       └── register.component.html
│   │   │   ├── documents/
│   │   │   │   ├── document-list/
│   │   │   │   │   ├── document-list.component.ts
│   │   │   │   │   └── document-list.component.html
│   │   │   │   ├── document-card/
│   │   │   │   │   ├── document-card.component.ts
│   │   │   │   │   └── document-card.component.html
│   │   │   │   └── document-form/
│   │   │   │       ├── document-form.component.ts
│   │   │   │       └── document-form.component.html
│   │   │   └── editor/
│   │   │       ├── editor.component.ts
│   │   │       ├── editor.component.html
│   │   │       ├── editor.component.css
│   │   │       ├── tiptap/
│   │   │       │   ├── tiptap-editor.directive.ts  # Tiptap integration
│   │   │       │   └── tiptap-extensions.ts       # Custom extensions
│   │   │       ├── yjs/
│   │   │       │   ├── signalr-provider.ts         # Custom Yjs SignalR provider
│   │   │       │   ├── yjs-document.service.ts     # Yjs doc lifecycle
│   │   │       │   └── awareness.service.ts        # Presence/cursor manager
│   │   │       └── presence/
│   │   │           ├── cursor/
│   │   │           │   └── cursor.component.ts
│   │   │           └── user-list/
│   │   │               ├── user-list.component.ts
│   │   │               └── user-list.component.html
│   │   └── shared/
│   │       ├── components/
│   │       │   ├── toolbar/
│   │       │   └── modal/
│   │       └── pipes/
│   └── assets/
└── proxy.conf.json                    # Angular dev proxy to API
```

## Milestones

### Milestone 1: Angular Scaffold + Tiptap Editor (Day 1–2)
- [ ] Initialize Angular 18 project with standalone components, signals
- [ ] Install dependencies: `yjs`, `@tiptap/angular`, `@tiptap/starter-kit`, `@tiptap/extension-collaboration`, `@tiptap/extension-history`, `@tiptap/extension-mention`, `rxjs`
- [ ] Create Tiptap editor component with starter kit (bold, italic, headings, lists, code block, links)
- [ ] Get single-user editing working with rich text formatting
- [ ] Set up Angular app structure: core, features, shared folders
- [ ] Create base styles and layout

### Milestone 2: Yjs + SignalR Real-Time Sync (Day 3–4)
- [ ] Create **custom Yjs SignalR provider** (`signalr-provider.ts`) — bridges Yjs updates ↔ SignalR hub
- [ ] Scaffold ASP.NET backend project with .NET 9
- [ ] Create `CollaborationHub` SignalR hub with:
  - `JoinDocument(string documentId)` — client joins a document room
  - `LeaveDocument(string documentId)` — client leaves
  - `SendUpdate(string documentId, byte[] update)` — relay Yjs updates
  - `SendAwareness(string documentId, string clientId, string awarenessData)` — relay awareness
  - `SendCursor(string documentId, string clientId, CursorPosition cursor)` — cursor position
- [ ] Wire up Yjs `ydoc` with the custom SignalR provider
- [ ] Test: open two browser tabs → type in both → see synced content
- [ ] Implement document room management (track connected clients per document)

### Milestone 3: Postgres Persistence + Document CRUD (Day 5)
- [ ] Set up PostgreSQL via Docker Compose / Aspire
- [ ] Create EF Core entities: `Document`, `DocumentSnapshot`, `DocumentVersion`, `User`
- [ ] Create `AppDbContext` with configuration
- [ ] Implement `DocumentService` with CRUD operations:
  - Create, read, update, delete documents
  - List user's documents
- [ ] Implement `SnapshotService` with debounced persistence:
  - Timer-based snapshot every 5 seconds (debounced)
  - Store Yjs doc as JSONB in `DocumentSnapshot` table
  - Version history: create version on explicit save
- [ ] Seed initial data (demo user, sample document)

### Milestone 4: Auth + Permissions + Presence (Day 6)
- [ ] Implement JWT auth:
  - Register endpoint (hash password with BCrypt)
  - Login endpoint (return access + refresh tokens)
  - Refresh token endpoint
  - JWT middleware with bearer token validation
- [ ] Create `RefreshToken` entity with sliding expiration
- [ ] Implement document permissions:
  - `DocumentPermission` entity (user_id, document_id, role: owner/editor/viewer)
  - Share document endpoint (invite by email/username)
  - Permission guard for editor component
  - Remove permission endpoint
- [ ] Implement Yjs Awareness over SignalR:
  - Send user name, cursor position, selection
  - Display remote cursors with colored carets and user names
  - Display online users list with avatars
  - Handle disconnect (remove cursor, update list)

### Milestone 5: Polish + Version History + Deploy (Day 7–8)
- [ ] Add document version history:
  - List versions endpoint
  - Restore version endpoint
  - Version history UI panel in editor
- [ ] Polish editor toolbar with all formatting options
- [ ] Add loading states, error handling, toast notifications
- [ ] Add document title editing inline
- [ ] Add "Share" modal with permission management
- [ ] Configure Angular dev proxy for local dev
- [ ] Write deployment guide
- [ ] Write README with architecture diagram

## Verification

### Manual Testing
1. **Auth flow**: Register → Login → see document list
2. **Document CRUD**: Create document → see in list → open → edit → delete
3. **Real-time sync**: Open document in two tabs → type in both → content syncs instantly
4. **Presence**: See other user's cursor in real-time, see user list with avatars
5. **Permissions**: Share document with another user → verify viewer can't edit
6. **Version history**: Create versions → list them → restore a version
7. **Offline recovery**: Disconnect tab → reconnect → content syncs from snapshot

### Unit Tests
- `AuthService` — token generation, validation, refresh
- `DocumentService` — CRUD operations
- `SnapshotService` — debounced persistence logic
- `PermissionService` — permission checks

### Integration Tests
- Document CRUD via HTTP endpoints
- Auth flow with JWT
- Document sharing flow

## Key Technical Details

### Custom Yjs SignalR Provider
```typescript
// signalr-provider.ts — The bridge between Yjs and SignalR
import * as Y from 'yjs';
import * as awarenessProtocol from 'y-protocols/awareness';

export class SignalRProvider {
  private connection: signalR.HubConnection;
  private ydoc: Y.Doc;
  private clientId: string;
  private awareness: awarenessProtocol.Awareness;

  constructor(url: string, documentId: string, ydoc: Y.Doc) {
    this.ydoc = ydoc;
    this.clientId = crypto.randomUUID();
    this.awareness = new awarenessProtocol.Awareness(ydoc);

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(url)
      .withAutomaticReconnect()
      .build();

    // Receive Yjs updates from other clients
    this.connection.on('ReceiveUpdate', (docId: string, update: ArrayBuffer) => {
      if (docId === documentId) {
        Y.applyUpdate(this.ydoc, new Uint8Array(update));
      }
    });

    // Receive awareness updates
    this.connection.on('ReceiveAwareness', (docId: string, data: ArrayBuffer) => {
      if (docId === documentId) {
        this.awareness.updateAwareness({
          clientID: this.clientId,
          awareness: new Uint8Array(data),
        });
      }
    });

    this.connection.start();
    this.ydoc.on('update', (update: Uint8Array) => {
      this.connection.invoke('SendUpdate', documentId, update.buffer);
    });
  }
}
```

### SignalR Hub (Backend)
```csharp
// CollaborationHub.cs
public class CollaborationHub : Hub
{
    private static readonly ConcurrentDictionary<string, HashSet<string>> _documentClients = new();
    private static readonly ConcurrentDictionary<string, Dictionary<string, byte[]>> _awarenessStates = new();

    public async Task JoinDocument(string documentId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, documentId);
        _documentClients.GetOrAdd(documentId, _ => new HashSet<string>()).Add(Context.ConnectionId);
    }

    public async Task SendUpdate(string documentId, byte[] update)
    {
        await Clients.Group(documentId).SendAsync("ReceiveUpdate", documentId, update);
    }

    public async Task SendAwareness(string documentId, byte[] awarenessData)
    {
        await Clients.Group(documentId).SendAsync("ReceiveAwareness", documentId, awarenessData);
    }

    public async Task SendCursor(string documentId, string clientId, string userName, int x, int y)
    {
        await Clients.Group(documentId).SendAsync("ReceiveCursor", documentId, clientId, userName, x, y);
    }
}
```

### Debounced Snapshot Service
```csharp
// SnapshotService.cs
public class SnapshotService
{
    private Timer? _snapshotTimer;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<Guid, DocumentSnapshot> _pendingSnapshots = new();

    public void Start(DocumentId documentId)
    {
        _snapshotTimer = new Timer(async _ =>
        {
            await SaveSnapshot(documentId);
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public void DocumentChanged(DocumentId documentId, byte[] ydocState)
    {
        _pendingSnapshots[documentId] = new DocumentSnapshot {
            DocumentId = documentId,
            State = ydocState,
            LastModified = DateTime.UtcNow
        };
    }

    private async Task SaveSnapshot(DocumentId documentId)
    {
        if (_pendingSnapshots.TryRemove(documentId, out var snapshot))
        {
            await _context.DocumentSnapshots.AddAsync(snapshot);
            await _context.SaveChangesAsync();
        }
    }
}
```
