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

  // Debounce: accumulate updates and send in batches
  private pendingUpdates: Uint8Array[] = [];
  private sendTimer: ReturnType<typeof setTimeout> | null = null;
  private readonly DEBOUNCE_MS = 500;

  constructor(
    private documentId: string,
    doc: Y.Doc,
    private signalr: SignalRService,
  ) {
    this.doc = doc;
    this.awareness = new Awareness(doc);

    // When the local Yjs doc changes, debounce and batch updates
    this.updateHandler = (update: Uint8Array, origin: unknown) => {
      if (origin !== 'remote') {
        console.log('[YjsProvider] Local update detected:', update.byteLength, 'bytes');
        this.pendingUpdates.push(update);
        this.scheduleSend();
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
   * Schedule sending accumulated updates after debounce delay.
   * If already scheduled, reset the timer.
   */
  private scheduleSend(): void {
    if (this.sendTimer) {
      clearTimeout(this.sendTimer);
    }
    this.sendTimer = setTimeout(async () => {
      if (this.pendingUpdates.length === 0) return;

      // Merge all pending updates into a single update
      const merged = Y.mergeUpdates(this.pendingUpdates);
      this.pendingUpdates = [];

      console.log('[YjsProvider] Sending merged update:', merged.byteLength, 'bytes');
      await this.signalr.sendYjsUpdate(this.documentId, merged).catch((err) => {
        console.error('[YjsProvider] Failed to send merged update:', err);
      });
    }, this.DEBOUNCE_MS);
  }

  /**
   * Save the current document state to the server before destroying.
   * This ensures no pending updates are lost.
   */
  async save(): Promise<void> {
    console.log('[YjsProvider] Saving document state...');
    // Encode the current Yjs state as a binary update
    const state = Y.encodeStateAsUpdate(this.doc);
    console.log('[YjsProvider] Encoded state:', state.byteLength, 'bytes');
    if (state && state.byteLength > 0) {
      await this.signalr.sendFullState(this.documentId, state);
      console.log('[YjsProvider] Save completed successfully');
    } else {
      console.warn('[YjsProvider] No state to save (empty document)');
    }
  }

  /**
   * Clean up all listeners and subscriptions.
   */
  destroy(): void {
    if (this.sendTimer) {
      clearTimeout(this.sendTimer);
      this.sendTimer = null;
    }
    this.doc.off('update', this.updateHandler);
    this.awareness.off('update', this.awarenessHandler);
    this.subscriptions.forEach((s) => s.unsubscribe());
    this.subscriptions = [];
    this.awareness.destroy();
  }
}
