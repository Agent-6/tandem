# Tandem — Implementation Tasks

## Phase 1: Foundation — Backend Scaffolding & Database (Day 1–2)
- [x] Create `Tandem.Api` project with required NuGet packages
- [x] Update `Tandem.AppHost` to wire PostgreSQL, Redis, API, and frontend
- [x] Update solution file to include new projects
- [x] Create domain entities (`ApplicationUser`, `Document`, `DocumentVersion`, `DocumentPermission`)
- [x] Create `TandemDbContext` with EF Core configuration
- [x] Create initial EF Core migration
- [x] Configure `Program.cs` (Identity, JWT, SignalR, CORS, EF Core)

## Phase 2: Document CRUD & REST API (Day 2–3)
- [x] Auth endpoints (register, login, refresh, me)
- [x] Document CRUD endpoints
- [x] Sharing endpoints

## Phase 3: SignalR Hub & Yjs Sync Protocol (Day 3–4)
- [x] `DocumentHub` SignalR hub
- [x] `DocumentSyncService` (server-side Yjs state + debounced persistence)
- [x] `PresenceService` (Redis-backed presence tracking)

## Phase 4: Angular Frontend Scaffolding (Day 1–2, parallel)
- [x] Scaffold Angular 19 app
- [x] Set up project structure (core, features, shared)
- [x] Global styles & design system
- [x] Routing configuration

## Phase 5: Collaborative Editor — The Core Feature (Day 3–5)
- [x] `SignalRService` (Angular)
- [x] `SignalRProvider` (custom Yjs ↔ SignalR bridge)
- [x] Editor component with Tiptap + Yjs collaboration
- [x] Rich text toolbar
- [x] Presence panel (live avatars)

## Phase 6: Auth UI & Dashboard (Day 5–6)
- [x] Login & Register pages
- [x] JWT interceptor & auth guard
- [x] Dashboard with document list
- [x] Create/delete document flows

## Phase 7: Version History & Sharing (Day 6–7)
- [ ] Version history panel
- [ ] Share dialog

## Phase 8: Polish & Production Readiness (Day 7–8)
- [ ] Dark mode & premium styling
- [ ] Animations & transitions
- [ ] Loading states & toasts
- [ ] Backend hardening
