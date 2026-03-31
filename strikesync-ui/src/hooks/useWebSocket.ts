// src/hooks/useWebSocket.ts

import { useEffect, useRef, useCallback } from 'react';
import { useGameStore, AnyWSMessage } from './useGameStore';

const WS_URL           = 'ws://localhost:8080/react';
const INITIAL_DELAY_MS = 1_000;
const MAX_DELAY_MS     = 16_000;
const BACKOFF_FACTOR   = 2;

// ─── Module-level send singleton ──────────────────────────────────────────
// Any component can call sendWS() without being inside a hook consumer.
let _sendFn: ((type: string, value?: unknown) => void) | null = null;

export function sendWS(type: string, value?: unknown): void {
  if (_sendFn) {
    _sendFn(type, value);
  } else {
    console.warn('[WS] sendWS called before socket was ready.');
  }
}

// ─── Hook ──────────────────────────────────────────────────────────────────
// Call this ONCE at the App root. All other components read state from
// useGameStore — they never need to call this hook themselves.

export function useWebSocket() {
  const wsRef         = useRef<WebSocket | null>(null);
  const reconnectMs   = useRef(INITIAL_DELAY_MS);
  const timerRef      = useRef<ReturnType<typeof setTimeout> | null>(null);
  const isMountedRef  = useRef(true);

  const applyEvent = useGameStore((s) => s.applyEvent);

  // ── send (stable ref, safe to put in dependency arrays) ─────────────────
  // ── send (stable ref, safe to put in dependency arrays) ─────────────────
  const send = useCallback((type: string, value?: unknown): void => {
    if (wsRef.current?.readyState === WebSocket.OPEN) {
      const msg = typeof value === 'object' && value !== null
        ? { type, ...value }
        : { type, value };
        
      wsRef.current.send(JSON.stringify(msg));
    } else {
      console.warn(`[WS] Cannot send "${type}" — socket not open.`);
    }
  }, []);

  // Expose as module singleton
  useEffect(() => {
    _sendFn = send;
    return () => { _sendFn = null; };
  }, [send]);

  // ── Pong reply when Unity pings ──────────────────────────────────────────
  useEffect(() => {
    const handlePong = () => send('pong');
    window.addEventListener('ws:pong', handlePong);
    return () => window.removeEventListener('ws:pong', handlePong);
  }, [send]);

  // ── connect ──────────────────────────────────────────────────────────────
  // We intentionally do NOT include `connect` in useEffect deps below —
  // the function is recreated on every render but we only want one connection.
  // The isMountedRef guard prevents any stale closure from taking effect.
  //
  // The ESLint exhaustive-deps warning for scheduleReconnect is resolved by
  // inlining the schedule logic directly inside connect() using a closure
  // over the refs, so there are no cross-function dependencies to declare.

  const connect = useCallback(() => {
    if (!isMountedRef.current) return;

    const socket = new WebSocket(WS_URL);
    wsRef.current = socket;

    socket.onopen = () => {
      if (!isMountedRef.current) return;
      console.log('[WS] Connected.');
      reconnectMs.current = INITIAL_DELAY_MS; // reset back-off
      applyEvent({ type: 'ws_connected' });
    };

    socket.onmessage = (e: MessageEvent<string>) => {
      if (!isMountedRef.current) return;
      try {
        const msg = JSON.parse(e.data) as AnyWSMessage;
        applyEvent(msg);
      } catch {
        console.warn('[WS] Received non-JSON message:', e.data);
      }
    };

    socket.onclose = (e: CloseEvent) => {
      if (!isMountedRef.current) return;
      console.log(`[WS] Closed (code ${e.code}). Retry in ${reconnectMs.current}ms…`);
      applyEvent({ type: 'ws_disconnected' });

      // ── Inline reconnect schedule ──────────────────────────────────────
      // Kept here (not extracted to scheduleReconnect) so this callback
      // only closes over refs and applyEvent — both stable — eliminating
      // the exhaustive-deps warning without needing // eslint-disable.
      if (timerRef.current) clearTimeout(timerRef.current);
      timerRef.current = setTimeout(() => {
        if (!isMountedRef.current) return;
        reconnectMs.current = Math.min(
          reconnectMs.current * BACKOFF_FACTOR,
          MAX_DELAY_MS,
        );
        connect();
      }, reconnectMs.current);
    };

    socket.onerror = () => {
      // onclose fires immediately after onerror; let it handle the retry.
    };
  }, [applyEvent]); // applyEvent comes from zustand — stable across renders

  // ── Lifecycle ─────────────────────────────────────────────────────────────
  useEffect(() => {
    isMountedRef.current = true;
    connect();

    return () => {
      isMountedRef.current = false;
      if (timerRef.current) clearTimeout(timerRef.current);
      wsRef.current?.close(1000, 'Component unmounted');
    };
    // connect is stable (useCallback with [applyEvent]) so this is safe.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return { send };
}