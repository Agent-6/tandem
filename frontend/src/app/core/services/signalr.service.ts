import { Injectable, NgZone } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { Subject, Observable } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { PresenceUser } from '../models';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class SignalRService {
  private connection: HubConnection | null = null;

  private readonly yjsUpdate$ = new Subject<Uint8Array>();
  private readonly awareness$ = new Subject<Uint8Array>();
  private readonly presence$ = new Subject<PresenceUser[]>();
  private readonly initialState$ = new Subject<Uint8Array | null>();
  private readonly connected$ = new Subject<boolean>();
  private readonly accessDenied$ = new Subject<string>();

  readonly onYjsUpdate: Observable<Uint8Array> = this.yjsUpdate$.asObservable();
  readonly onAwareness: Observable<Uint8Array> = this.awareness$.asObservable();
  readonly onPresence: Observable<PresenceUser[]> = this.presence$.asObservable();
  readonly onInitialState: Observable<Uint8Array | null> = this.initialState$.asObservable();
  readonly onConnected: Observable<boolean> = this.connected$.asObservable();
  readonly onAccessDenied: Observable<string> = this.accessDenied$.asObservable();

  constructor(
    private auth: AuthService,
    private zone: NgZone,
  ) {}

  async connect(documentId: string): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) {
      await this.disconnect();
    }

    this.connection = new HubConnectionBuilder()
      .withUrl(environment.hubUrl, {
        accessTokenFactory: () => this.auth.getToken() ?? '',
      })
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000])
      .configureLogging(LogLevel.Information)
      .build();

    this.registerHandlers();

    try {
      await this.connection.start();
      this.zone.run(() => this.connected$.next(true));

      // Join the document room - initial state will be received via DocumentState event
      await this.connection.invoke('JoinDocument', documentId);
    } catch (err) {
      console.error('[SignalR] Connection failed:', err);
      this.zone.run(() => this.connected$.next(false));
      throw err;
    }

    this.connection.onreconnected(() => {
      this.zone.run(() => this.connected$.next(true));
      // Re-join the document on reconnect
      this.connection?.invoke('JoinDocument', documentId).catch(console.error);
    });

    this.connection.onclose(() => {
      this.zone.run(() => this.connected$.next(false));
    });
  }

  async disconnect(): Promise<void> {
    if (this.connection) {
      await this.connection.stop();
      this.connection = null;
    }
  }

  async sendYjsUpdate(documentId: string, update: Uint8Array): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) {
      const base64 = this.uint8ArrayToBase64(update);
      console.log('[SignalR] Sending Yjs update:', update.byteLength, 'bytes');
      await this.connection.invoke('SendYjsUpdate', documentId, base64);
      console.log('[SignalR] Yjs update sent successfully');
    } else {
      console.error('[SignalR] Cannot send Yjs update: connection not connected (state:', this.connection?.state, ')');
      throw new Error('SignalR connection not established');
    }
  }

  async sendAwareness(documentId: string, update: Uint8Array): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) {
      const base64 = this.uint8ArrayToBase64(update);
      await this.connection.invoke('SendAwareness', documentId, base64);
    } else {
      console.warn('[SignalR] Cannot send awareness update: connection not connected (state:', this.connection?.state, ')');
    }
  }

  /**
   * Forces an immediate save of the document state to the database.
   */
  async saveDocument(documentId: string): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) {
      console.log('[SignalR] Sending SaveDocument request for:', documentId);
      await this.connection.invoke('SaveDocument', documentId);
      console.log('[SignalR] SaveDocument request sent successfully');
    } else {
      console.error('[SignalR] Cannot save document: connection not connected (state:', this.connection?.state, ')');
      throw new Error('SignalR connection not established');
    }
  }

  /**
   * Sends a full Yjs state sync to the server.
   */
  async sendFullState(documentId: string, fullState: Uint8Array): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) {
      const base64 = this.uint8ArrayToBase64(fullState);
      console.log('[SignalR] Sending full state:', fullState.byteLength, 'bytes');
      await this.connection.invoke('SendFullState', documentId, base64);
      console.log('[SignalR] Full state sent successfully');
    } else {
      console.error('[SignalR] Cannot send full state: connection not connected (state:', this.connection?.state, ')');
      throw new Error('SignalR connection not established');
    }
  }

  private registerHandlers(): void {
    if (!this.connection) return;

    this.connection.on('DocumentState', (base64: string) => {
      console.log('[SignalR] DocumentState handler called, base64:', base64 ? `${base64.length} chars` : 'null/undefined');
      this.zone.run(() => {
        if (base64) {
          const state = this.base64ToUint8Array(base64);
          console.log('[SignalR] Decoded DocumentState:', state.byteLength, 'bytes');
          this.initialState$.next(state);
        } else {
          console.log('[SignalR] DocumentState is empty/null');
          this.initialState$.next(null);
        }
      });
    });

    this.connection.on('ReceiveYjsUpdate', (base64: string) => {
      this.zone.run(() => {
        this.yjsUpdate$.next(this.base64ToUint8Array(base64));
      });
    });

    this.connection.on('ReceiveAwareness', (base64: string) => {
      this.zone.run(() => {
        this.awareness$.next(this.base64ToUint8Array(base64));
      });
    });

    this.connection.on('ReceivePresence', (users: PresenceUser[]) => {
      this.zone.run(() => {
        // Ensure each user has both displayName and name for compatibility
        const normalizedUsers = users.map(user => ({
          ...user,
          name: user.name || user.displayName,
          displayName: user.displayName || user.name,
        }));
        this.presence$.next(normalizedUsers);
      });
    });

    this.connection.on('PresenceUpdate', (users: PresenceUser[]) => {
      this.zone.run(() => {
        const normalizedUsers = users.map(user => ({
          ...user,
          name: user.name || user.displayName,
          displayName: user.displayName || user.name,
        }));
        this.presence$.next(normalizedUsers);
      });
    });

    this.connection.on('AccessDenied', (documentId: string) => {
      this.zone.run(() => {
        this.accessDenied$.next(documentId);
      });
    });
  }

  private uint8ArrayToBase64(data: Uint8Array): string {
    let binary = '';
    for (let i = 0; i < data.byteLength; i++) {
      binary += String.fromCharCode(data[i]);
    }
    return btoa(binary);
  }

  private base64ToUint8Array(base64: string): Uint8Array {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
      bytes[i] = binary.charCodeAt(i);
    }
    return bytes;
  }
}
