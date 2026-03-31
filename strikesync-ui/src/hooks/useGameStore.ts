// src/hooks/useGameStore.ts
//
// PROTOCOL: All messages are FLAT — no nested "value" objects.
//
// For backward compat during migration, state_change accepts BOTH:
//   { type: "state_change", state: "fighting" }   ← new canonical form
//   { type: "state_change", value: "fighting" }   ← legacy form (still works)
// The store reads  msg.state ?? msg.value  so either shape works.

import { create, StateCreator } from 'zustand';

// ─── Types ─────────────────────────────────────────────────────────────────

export type GameScreen =
  | 'menu'
  | 'char_select'
  | 'map_select'
  | 'fight_intro'
  | 'countdown'
  | 'fighting'
  | 'game_over';

export interface PlayerState {
  hp:   number;
  pct:  number;
  name: string;
  icon: string;
}

export interface HitEvent {
  id:        number;
  player:    number;
  timestamp: number;
}

export interface GameStore {
  // Connection
  wsConnected:    boolean;
  unityConnected: boolean;

  // Game
  screen:      GameScreen;
  countdown:   string;
  winner:      'player1' | 'player2' | null;
  selectedMap: string | null;

  // Players
  players: [PlayerState, PlayerState];

  // Hit events ring buffer (last 20)
  hitEvents: HitEvent[];

  // Actions
  setScreen:  (s: GameScreen) => void;
  applyEvent: (msg: AnyWSMessage) => void;
}

// ─── Raw message shapes ────────────────────────────────────────────────────
//
// FLAT PROTOCOL — every field is a top-level key. No nested objects.
// Both "state"/"value" and "countdown"/"value" and "winner"/"value"
// are accepted (during migration from the legacy overloaded "value" field).

export type AnyWSMessage =
  // Unity → React (new canonical form)
  | { type: 'state_change';   state?: string;    value?: string }
  | { type: 'countdown';      countdown?: string; value?: string }
  | { type: 'health_update';  player: number; hp: number; pct: number }
  | { type: 'game_over';      winner?: string;    value?: string }
  | { type: 'hit_event';      player: number }
  // Bridge meta
  | { type: 'unity_connected' }
  | { type: 'unity_disconnected' }
  | { type: 'ws_connected' }
  | { type: 'ws_disconnected' }
  | { type: 'ping' }
  // Fallback
  | { type: string; [k: string]: unknown };

// ─── Helpers ───────────────────────────────────────────────────────────────

const RING_BUFFER_SIZE = 20;
let _hitEventCounter   = 0;

function pushHitEvent(events: HitEvent[], player: number): HitEvent[] {
  const next = [
    ...events,
    { id: ++_hitEventCounter, player, timestamp: Date.now() },
  ];
  return next.length > RING_BUFFER_SIZE ? next.slice(-RING_BUFFER_SIZE) : next;
}

const defaultPlayer = (name: string): PlayerState => ({
  hp: 100, pct: 1, name, icon: '',
});

// Reads the canonical field and falls back to the legacy "value" field.
// Handles both old { value: "fighting" } and new { state: "fighting" } shapes.
function coalesce(msg: Record<string, unknown>, ...keys: string[]): string | undefined {
  for (const k of keys) {
    const v = msg[k];
    if (typeof v === 'string' && v.length > 0) return v;
  }
  return undefined;
}

// ─── Store ─────────────────────────────────────────────────────────────────

const storeCreator: StateCreator<GameStore> = (set, get) => ({
  wsConnected:    false,
  unityConnected: false,
  screen:         'menu' as GameScreen,
  countdown:      '',
  winner:         null,
  selectedMap:    null,
  hitEvents:      [],
  players: [
    defaultPlayer('Player 1'),
    defaultPlayer('Player 2'),
  ],

  setScreen: (screen: GameScreen) => set({ screen }),

  applyEvent: (msg: AnyWSMessage) => {
    const raw = msg as Record<string, unknown>;

    switch (msg.type) {

      // ── Connection meta ─────────────────────────────────────────────────
      case 'ws_connected':
        set({ wsConnected: true });
        break;

      case 'ws_disconnected':
        set({ wsConnected: false });
        break;

      case 'unity_connected':
        set({ unityConnected: true });
        break;

      case 'unity_disconnected':
        set({ unityConnected: false });
        break;

      // ── State change ────────────────────────────────────────────────────
      // Accepts BOTH { state: "X" } and { value: "X" } for backward compat.
      case 'state_change': {
        const stateStr = coalesce(raw, 'state', 'value');
        if (!stateStr) break;

        // Sub-type: player_meta (still uses pipe-delimited value for now)
        if (stateStr.startsWith('player_meta|')) {
          const parts   = stateStr.split('|');
          const players = [...get().players] as [PlayerState, PlayerState];
          players[0]    = { ...players[0], name: parts[1] ?? 'Player 1' };
          players[1]    = { ...players[1], name: parts[2] ?? 'Player 2' };
          set({ players });
          return;
        }

        const screenMap: Record<string, GameScreen> = {
          menu:        'menu',
          char_select: 'char_select',
          map_select:  'map_select',
          fight_intro: 'fight_intro',
          countdown:   'countdown',
          fighting:    'fighting',
          game_over:   'game_over',
        };
        const next = screenMap[stateStr];
        if (next) set({ screen: next });
        break;
      }

      // ── Countdown ───────────────────────────────────────────────────────
      // Accepts BOTH { countdown: "3" } and legacy { value: "3" }.
      case 'countdown': {
        const val = coalesce(raw, 'countdown', 'value') ?? '';
        set({ countdown: val });
        break;
      }

      // ── Health update ───────────────────────────────────────────────────
      // Always flat: { player: 0, hp: 87.5, pct: 0.875 }
      case 'health_update': {
        const { player, hp, pct } = msg as Extract<AnyWSMessage, { type: 'health_update' }>;
        if (player !== 0 && player !== 1) break;

        const players = [...get().players] as [PlayerState, PlayerState];
        players[player] = { ...players[player], hp, pct };
        set({ players, hitEvents: pushHitEvent(get().hitEvents, player) });
        break;
      }

      // ── Game over ───────────────────────────────────────────────────────
      // Accepts BOTH { winner: "player1" } and legacy { value: "player1" }.
      case 'game_over': {
        const winnerStr = coalesce(raw, 'winner', 'value') as 'player1' | 'player2' | undefined;
        if (!winnerStr) break;
        set({ winner: winnerStr, screen: 'game_over' });
        break;
      }

      // ── Hit event ───────────────────────────────────────────────────────
      case 'hit_event': {
        const { player } = msg as Extract<AnyWSMessage, { type: 'hit_event' }>;
        set({ hitEvents: pushHitEvent(get().hitEvents, player) });
        break;
      }

      // ── Ping from bridge ────────────────────────────────────────────────
      case 'ping':
        window.dispatchEvent(new CustomEvent('ws:pong'));
        break;

      default:
        break;
    }
  },
});

export const useGameStore = create<GameStore>(storeCreator);