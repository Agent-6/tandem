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
      await this.connection.invoke('SendYjsUpdate', documentId, base64);
    }
  }

  async sendAwareness(documentId: string, update: Uint8Array): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) {
      const base64 = this.uint8ArrayToBase64(update);
      await this.connection.invoke('SendAwareness', documentId, base64);
    }
  }

  private registerHandlers(): void {
    if (!this.connection) return;

    this.connection.on('DocumentState', (base64: string) => {
      this.zone.run(() => {
        if (base64) {
          this.initialState$.next(this.base64ToUint8Array(base64));
        } else {
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
