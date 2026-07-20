import * as Y from 'yjs';
import { Awareness, encodeAwarenessUpdate, applyAwarenessUpdate } from 'y-protocols/awareness';
import { Subscription } from 'rxjs';
import { SignalRService } from './signalr.service';

/**
 * Custom Yjs Provider that bridges Yjs document sync and awareness
 * over SignalR instead of WebSocket (y-websocket).
 *
 * This is the core of our real-time collaboration:
 * - Local Yjs changes → sent to server via SignalR
 * - Remote SignalR updates → applied to local Yjs doc
 * - Awareness (cursors, selections, user presence) → bidirectional
 */
export class SignalRProvider {
  doc: Y.Doc;
  awareness: Awareness;
  private subscriptions: Subscription[] = [];
  private updateHandler: (update: Uint8Array, origin: unknown) => void;
  private awarenessHandler: (
    changes: { added: number[]; updated: number[]; removed: number[] },
    origin: unknown,
  ) => void;

  constructor(
    private documentId: string,
    doc: Y.Doc,
    private signalr: SignalRService,
  ) {
    this.doc = doc;
    this.awareness = new Awareness(doc);

    // When the local Yjs doc changes, send the update to other clients via SignalR
    this.updateHandler = (update: Uint8Array, origin: unknown) => {
      if (origin !== 'remote') {
        this.signalr.sendYjsUpdate(this.documentId, update).catch(console.error);
      }
    };
    this.doc.on('update', this.updateHandler);

    // When SignalR receives a remote update, apply it to the local Yjs doc
    this.subscriptions.push(
      this.signalr.onYjsUpdate.subscribe((update) => {
        Y.applyUpdate(this.doc, update, 'remote');
      }),
    );

    // Awareness: local changes → SignalR
    this.awarenessHandler = ({ added, updated, removed }) => {
      const changedClients = [...added, ...updated, ...removed];
      const update = encodeAwarenessUpdate(this.awareness, changedClients);
      this.signalr.sendAwareness(this.documentId, update).catch(console.error);
    };
    this.awareness.on('update', this.awarenessHandler);

    // Awareness: SignalR → local
    this.subscriptions.push(
      this.signalr.onAwareness.subscribe((update) => {
        applyAwarenessUpdate(this.awareness, update, 'remote');
      }),
    );
  }

  /**
   * Set local user awareness state (cursor, selection, user info).
   */
  setLocalState(state: Record<string, unknown>): void {
    this.awareness.setLocalStateField('user', state);
  }

  /**
   * Clean up all listeners and subscriptions.
   */
  destroy(): void {
    this.doc.off('update', this.updateHandler);
    this.awareness.off('update', this.awarenessHandler);
    this.subscriptions.forEach((s) => s.unsubscribe());
    this.subscriptions = [];
    this.awareness.destroy();
  }
}
