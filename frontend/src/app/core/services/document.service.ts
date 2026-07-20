import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  Document,
  DocumentListItem,
  DocumentVersion,
  DocumentPermission,
  CreateDocumentRequest,
  ShareDocumentRequest,
} from '../models';

@Injectable({ providedIn: 'root' })
export class DocumentService {
  private readonly baseUrl = `${environment.apiUrl}/documents`;

  constructor(private http: HttpClient) {}

  // --- Document CRUD ---

  getAll(): Observable<Document[]> {
    return this.http.get<{ Owned: DocumentListItem[]; Shared: DocumentListItem[] }>(this.baseUrl).pipe(
      map(response => [...response.Owned, ...response.Shared].map(item => ({
        id: item.id,
        title: item.title,
        ownerId: item.ownerId,
        ownerDisplayName: item.ownerName,
        createdAt: item.createdAt,
        updatedAt: item.updatedAt,
        isOwner: item.isOwner,
        role: item.isOwner ? 'Owner' : 'Editor', // Default role for list view
        collaborators: item.collaborators
      })))
    );
  }

  getById(id: string): Observable<Document> {
    return this.http.get<Document>(`${this.baseUrl}/${id}`);
  }

  create(data: CreateDocumentRequest): Observable<Document> {
    return this.http.post<Document>(this.baseUrl, data).pipe(
      map(doc => ({
        ...doc,
        ownerDisplayName: (doc as any).ownerName || doc.ownerDisplayName
      }))
    );
  }

  updateTitle(id: string, title: string): Observable<void> {
    return this.http.patch<void>(`${this.baseUrl}/${id}`, { title });
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  // --- Versions ---

  getVersions(documentId: string): Observable<DocumentVersion[]> {
    return this.http.get<DocumentVersion[]>(`${this.baseUrl}/${documentId}/versions`);
  }

  // --- Sharing ---

  getPermissions(documentId: string): Observable<DocumentPermission[]> {
    return this.http.get<DocumentPermission[]>(`${this.baseUrl}/${documentId}/share`);
  }

  share(documentId: string, email: string, role: string): Observable<DocumentPermission> {
    return this.http.post<DocumentPermission>(
      `${this.baseUrl}/${documentId}/share`,
      { email, role },
    );
  }

  updateShare(documentId: string, userId: string, role: string): Observable<void> {
    return this.http.put<void>(
      `${this.baseUrl}/${documentId}/share/${userId}`,
      { role },
    );
  }

  removeShare(documentId: string, userId: string): Observable<void> {
    return this.http.delete<void>(
      `${this.baseUrl}/${documentId}/share/${userId}`,
    );
  }
}
