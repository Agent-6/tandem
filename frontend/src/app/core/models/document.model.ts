export interface Document {
  id: string;
  title: string;
  ownerId: string;
  ownerDisplayName: string;
  createdAt: string;
  updatedAt: string;
  isOwner: boolean;
  role: string;
  collaborators?: CollaboratorInfo[];
}

export interface DocumentListItem {
  id: string;
  title: string;
  ownerId: string;
  ownerName: string;
  createdAt: string;
  updatedAt: string;
  isOwner: boolean;
  collaborators: CollaboratorInfo[];
}

export interface CollaboratorInfo {
  userId: string;
  displayName: string;
  avatarColor: string;
  role: string;
}

export interface DocumentVersion {
  id: string;
  documentId: string;
  versionNumber: number;
  createdAt: string;
  createdByUserId: string;
  createdByDisplayName: string;
}

export enum DocumentRole {
  Owner = 'Owner',
  Editor = 'Editor',
  Viewer = 'Viewer',
}

export interface DocumentPermission {
  id: string;
  documentId: string;
  userId: string;
  userEmail: string;
  userDisplayName: string;
  role: DocumentRole;
}

export interface PresenceUser {
  userId: string;
  displayName: string;
  name: string;
  avatarColor: string;
  cursorPosition?: CursorPosition;
}

export interface CursorPosition {
  anchor: number;
  head: number;
}

export interface CreateDocumentRequest {
  title: string;
}

export interface ShareDocumentRequest {
  email: string;
  role: DocumentRole;
}
