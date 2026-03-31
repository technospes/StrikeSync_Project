// WSTypes.cs — single source of truth for the WebSocket protocol.
//
// PROTOCOL DESIGN: ALL messages are FLAT. Never use nested "value" objects.
//
// Unity → React examples:
//   { "type": "state_change",   "state": "fighting" }
//   { "type": "countdown",      "countdown": "3" }
//   { "type": "health_update",  "player": 0, "hp": 87.5, "pct": 0.875 }
//   { "type": "game_over",      "winner": "player1" }
//   { "type": "hit_event",      "player": 1 }
//   { "type": "ping" }
//
// React → Unity examples:
//   { "type": "select_character",  "player": 0, "name": "PHANTOM" }
//   { "type": "confirm_character", "player": 1, "name": "WARBOT" }
//   { "type": "map_selected",      "map": 2,    "mapName": "THE VOID" }
//   { "type": "start_game" }
//   { "type": "rematch" }
//   { "type": "main_menu" }
//   { "type": "goto_char_select" }

using System;
using System.Collections.Generic;

// ─── Event type enum ──────────────────────────────────────────────────────────
public enum WSEventType
{
    // Unity → React
    StateChange,
    HealthUpdate,
    Countdown,
    GameOver,
    HitEvent,
    Ping,

    // React → Unity
    SelectCharacter,
    ConfirmCharacter,
    StartGame,
    MapSelected,      // React sends chosen map after slot spin
    Rematch,
    MainMenu,
    Pong,
    GotoCharSelect,

    Unknown
}

// ─── Wire string mappings ──────────────────────────────────────────────────────
public static class WSEventTypeExtensions
{
    private static readonly Dictionary<WSEventType, string> ToWire = new()
    {
        { WSEventType.StateChange,      "state_change"      },
        { WSEventType.HealthUpdate,     "health_update"     },
        { WSEventType.Countdown,        "countdown"         },
        { WSEventType.GameOver,         "game_over"         },
        { WSEventType.HitEvent,         "hit_event"         },
        { WSEventType.Ping,             "ping"              },
        { WSEventType.SelectCharacter,  "select_character"  },
        { WSEventType.ConfirmCharacter, "confirm_character" },
        { WSEventType.StartGame,        "start_game"        },
        { WSEventType.MapSelected,      "map_selected"      },
        { WSEventType.Rematch,          "rematch"           },
        { WSEventType.MainMenu,         "main_menu"         },
        { WSEventType.Pong,             "pong"              },
        { WSEventType.GotoCharSelect,   "goto_char_select"  },
        { WSEventType.Unknown,          "unknown"           },
    };

    private static readonly Dictionary<string, WSEventType> FromWire;

    static WSEventTypeExtensions()
    {
        FromWire = new Dictionary<string, WSEventType>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in ToWire) FromWire[kv.Value] = kv.Key;
    }

    public static string ToWireString(this WSEventType t) =>
        ToWire.TryGetValue(t, out var s) ? s : "unknown";

    public static WSEventType ToEventType(this string s) =>
        FromWire.TryGetValue(s ?? "", out var t) ? t : WSEventType.Unknown;
}

// ─── Inbound message model ────────────────────────────────────────────────────
//
// IMPROVEMENT: Each semantic field is typed explicitly — no more overloaded
// string "value" that meant different things depending on the event type.
// This eliminates parsing ambiguity and makes switch-case code self-documenting.
//
// IMPROVEMENT: player and map have safety defaults of -1 so you can always
// guard with  if (msg.player < 0) return;  rather than relying on 0 being valid.
[Serializable]
public class WSMessage
{
    // ── Always present ────────────────────────────────────────────────────
    public string type;

    // ── state_change ──────────────────────────────────────────────────────
    // Previously: { "type": "state_change", "value": "fighting" }
    // Now:        { "type": "state_change", "state": "fighting" }
    // "value" is kept as a legacy alias — both are accepted during migration.
    public string state;          // canonical field
    public string value;          // legacy alias — Unity-side code reads state ?? value

    // ── countdown ─────────────────────────────────────────────────────────
    // Previously: { "type": "countdown", "value": "3" }
    // Now:        { "type": "countdown", "countdown": "3" }
    public string countdown;

    // ── game_over ─────────────────────────────────────────────────────────
    // Previously: { "type": "game_over", "value": "player1" }
    // Now:        { "type": "game_over", "winner": "player1" }
    public string winner;

    // ── health_update ─────────────────────────────────────────────────────
    // { "type": "health_update", "player": 0, "hp": 87.5, "pct": 0.875 }
    public float hp;
    public float pct;

    // ── select_character / confirm_character ──────────────────────────────
    // { "type": "select_character", "player": 0, "name": "PHANTOM" }
    public string name;

    // ── map_selected ──────────────────────────────────────────────────────
    // { "type": "map_selected", "map": 2, "mapName": "THE VOID" }
    public string mapName;

    // ── Shared int fields — safety defaults prevent silent zero-value bugs ──
    public int player = -1;       // -1 = unassigned
    public int map = -1;       // -1 = unassigned

    // ── Helpers ───────────────────────────────────────────────────────────

    // Canonical state string — falls back to legacy "value" field for backward compat.
    public string State => state ?? value;
    public string Countdown => countdown ?? value;
    public string Winner => winner ?? value;

    // Cached event type — resolved once per message
    private WSEventType? _eventType;
    public WSEventType EventType =>
        _eventType ??= (_eventType = type.ToEventType()).Value;
}

// ─── Game state string constants ──────────────────────────────────────────────
public static class GameState
{
    public const string Menu = "menu";
    public const string CharSelect = "char_select";
    public const string MapSelect = "map_select";
    public const string Countdown = "countdown";
    public const string Fighting = "fighting";
    public const string GameOver = "game_over";
}