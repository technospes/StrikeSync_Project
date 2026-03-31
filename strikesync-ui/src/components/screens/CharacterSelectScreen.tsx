// src/components/screens/CharacterSelectScreen.tsx
//
// Full dual-player character selection screen.
// P1 = left half (purple accent), P2 = right half (blue accent)
// Characters come from Unity via WebSocket — the CHARACTERS array below
// should match your actual CharacterData ScriptableObjects by name.
// When both players confirm, sends "start_game" to Unity.

import {
  useState, useEffect, useRef, useCallback,
} from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { sendWS } from '../../hooks/useWebSocket';

// ─── Character data ─────────────────────────────────────────────────────────
// These names MUST match CharacterData.characterName in Unity exactly.
// Add/remove entries to match your actual character roster.
// ─── Character data ─────────────────────────────────────────────────────────
export interface CharacterDef {
  id:       number;
  name:     string;
  unityName: string; // <-- ADDED THIS FOR UNITY
  subtitle: string;
  accentH:  string;  
  accentB:  string;  
  stats: { STR: number; AGI: number; RNG: number };
  bodyHue:  number;
}

export const CHARACTERS: CharacterDef[] = [
  { id:0, name:'CLOWN',     unityName:'Art: Clown', subtitle:'THE JESTER',       accentH:'#A855F7', accentB:'#EC4899', stats:{STR:85,AGI:70,RNG:60}, bodyHue:280 },
  { id:1, name:'MUTANT',    unityName:'Donatello',    subtitle:'TOXIC BRAWLER',    accentH:'#10B981', accentB:'#34D399', stats:{STR:95,AGI:40,RNG:50}, bodyHue:160 },
  { id:2, name:'HAWK',      unityName:'Lady Hawk',            subtitle:'AERIAL STRIKER',   accentH:'#3B82F6', accentB:'#06B6D4', stats:{STR:60,AGI:95,RNG:40}, bodyHue:215 },
  { id:3, name:'FIGHTER',   unityName:'Sophie',    subtitle:'MARTIAL ARTIST',   accentH:'#EC4899', accentB:'#F472B6', stats:{STR:75,AGI:80,RNG:55}, bodyHue:330 },
  { id:4, name:'WARMACHINE',unityName:'Rhodey',subtitle:'HEAVY ARTILLERY',  accentH:'#EF4444', accentB:'#F87171', stats:{STR:100,AGI:30,RNG:85},bodyHue:0   },
  { id:5, name:'ZOMBIE',    unityName:'Penny', subtitle:'UNDEAD HORROR',    accentH:'#8B5CF6', accentB:'#A78BFA', stats:{STR:80,AGI:50,RNG:20}, bodyHue:260 },
  { id:6, name:'BRAWLER',   unityName:'Zack',      subtitle:'STREET ENFORCER',  accentH:'#F59E0B', accentB:'#FCD34D', stats:{STR:80,AGI:70,RNG:50}, bodyHue:40  },
];

// ─── Types ───────────────────────────────────────────────────────────────────
interface PlayerState {
  selectedIdx: number;
  confirmed:   boolean;
}

// ─── Easing ──────────────────────────────────────────────────────────────────
const EASE: [number,number,number,number] = [0.22, 1, 0.36, 1];

// ─── Root component ──────────────────────────────────────────────────────────
export function CharacterSelectScreen() {
  const [p1, setP1] = useState<PlayerState>({ selectedIdx: 0, confirmed: false });
  const [p2, setP2] = useState<PlayerState>({ selectedIdx: 1, confirmed: false });

  // Track previous index to drive the fade-swap animation
  const [p1Prev, setP1Prev] = useState(0);
  const [p2Prev, setP2Prev] = useState(1);

  const selectChar = useCallback((player: 1|2, idx: number) => {
    if (player === 1) {
      if (p1.confirmed) return;
      setP1Prev(p1.selectedIdx);
      setP1(s => ({ ...s, selectedIdx: idx }));
      // Tell Unity to show this character using the exact prefab name
      sendWS('select_character', { player: 0, name: CHARACTERS[idx].unityName });
    } else {
      if (p2.confirmed) return;
      setP2Prev(p2.selectedIdx);
      setP2(s => ({ ...s, selectedIdx: idx }));
      sendWS('select_character', { player: 1, name: CHARACTERS[idx].unityName });
    }
  }, [p1, p2]);

  const confirm = useCallback((player: 1|2) => {
    if (player === 1) {
      setP1(s => ({ ...s, confirmed: true }));
      sendWS('confirm_character', { player: 0, name: CHARACTERS[p1.selectedIdx].unityName });
    } else {
      setP2(s => ({ ...s, confirmed: true }));
      sendWS('confirm_character', { player: 1, name: CHARACTERS[p2.selectedIdx].unityName });
    }
  }, [p1.selectedIdx, p2.selectedIdx]);

  // When BOTH players confirm — fire start_game
  useEffect(() => {
    if (p1.confirmed && p2.confirmed) {
      const t = setTimeout(() => sendWS('start_game'), 900);
      return () => clearTimeout(t);
    }
  }, [p1.confirmed, p2.confirmed]);

  const bothReady = p1.confirmed && p2.confirmed;

  return (
    <motion.div
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      transition={{ duration: 0.35 }}
      style={{
        position: 'fixed', inset: 0,
        background: '#050A1A',
        fontFamily: '"Rajdhani", monospace',
        display: 'flex', flexDirection: 'column',
        overflow: 'hidden',
      }}
    >
      {/* Background diagonal lines */}
      <div style={{
        position: 'absolute', inset: 0,
        backgroundImage: 'repeating-linear-gradient(135deg,transparent,transparent 38px,rgba(168,85,247,.018) 38px,rgba(168,85,247,.018) 39px)',
        pointerEvents: 'none', zIndex: 0,
      }} />

      {/* Top bar */}
      <TopBar p1={p1} p2={p2} />

      {/* Split arena */}
      <div style={{ display: 'flex', flex: 1, overflow: 'hidden', position: 'relative', zIndex: 1 }}>

        {/* P1 panel */}
        <PlayerPanel
          player={1}
          state={p1}
          prevIdx={p1Prev}
          onSelect={(i) => selectChar(1, i)}
          onConfirm={() => confirm(1)}
        />

        {/* Center divider */}
        <Divider bothReady={bothReady} />

        {/* P2 panel */}
        <PlayerPanel
          player={2}
          state={p2}
          prevIdx={p2Prev}
          onSelect={(i) => selectChar(2, i)}
          onConfirm={() => confirm(2)}
        />
      </div>

      {/* Both ready overlay */}
      <AnimatePresence>
        {bothReady && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            style={{
              position: 'absolute', inset: 0, zIndex: 100,
              display: 'flex', alignItems: 'center', justifyContent: 'center',
              background: 'rgba(5,10,26,.6)',
              backdropFilter: 'blur(4px)',
              pointerEvents: 'none',
            }}
          >
            <motion.div
              initial={{ scale: 0.7, opacity: 0 }}
              animate={{ scale: 1, opacity: 1 }}
              transition={{ delay: 0.1, duration: 0.5, ease: EASE }}
              style={{
                fontFamily: '"Orbitron", monospace',
                fontSize: 48, fontWeight: 900, fontStyle: 'italic',
                background: 'linear-gradient(135deg,#A855F7,#EC4899,#3B82F6)',
                WebkitBackgroundClip: 'text',
                WebkitTextFillColor: 'transparent',
                backgroundClip: 'text',
                letterSpacing: '0.06em',
                textShadow: 'none',
              }}
            >
              FIGHT!
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>
    </motion.div>
  );
}

// ─── TopBar ──────────────────────────────────────────────────────────────────
function TopBar({ p1, p2 }: { p1: PlayerState; p2: PlayerState }) {
  return (
    <motion.div
      initial={{ y: -40, opacity: 0 }}
      animate={{ y: 0, opacity: 1 }}
      transition={{ duration: 0.4, ease: EASE }}
      style={{
        display: 'flex', alignItems: 'center',
        padding: '0 20px', height: 48,
        background: 'rgba(5,10,26,.92)',
        borderBottom: '1px solid rgba(255,255,255,.06)',
        flexShrink: 0, zIndex: 10, position: 'relative',
        gap: 16,
      }}
    >
      {/* Logo */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        <span style={{
          fontFamily: '"Orbitron", monospace',
          fontSize: 15, fontWeight: 900, fontStyle: 'italic',
          background: 'linear-gradient(135deg,#C084FC,#EC4899)',
          WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent',
          backgroundClip: 'text', letterSpacing: '0.04em',
        }}>STRIKESYNC</span>
        <span style={{ fontSize: 10, color: 'rgba(168,130,220,.55)', letterSpacing: '0.22em' }}>
          SELECTING STRIKER
        </span>
      </div>

      {/* Center status (Absolutely centered) */}
      <div style={{
        position: 'absolute', 
        left: '50%', 
        transform: 'translateX(-50%)',
        display: 'flex', 
        alignItems: 'center', 
        justifyContent: 'center', 
        gap: 24,
      }}>
        <PlayerStatusPill player={1} confirmed={p1.confirmed} charIdx={p1.selectedIdx} />
        <div style={{ width: 1, height: 18, background: 'rgba(255,255,255,.1)' }} />
        <PlayerStatusPill player={2} confirmed={p2.confirmed} charIdx={p2.selectedIdx} />
      </div>

      {/* Settings icon */}
      <div style={{
        width: 30, height: 30, border: '1px solid rgba(255,255,255,.14)',
        borderRadius: 6, display: 'flex', alignItems: 'center', justifyContent: 'center',
        cursor: 'pointer',
      }}>
        <svg width="14" height="14" viewBox="0 0 14 14" fill="none">
          <circle cx="7" cy="7" r="2.5" stroke="rgba(255,255,255,.45)" strokeWidth="1"/>
          <path d="M7 1v2M7 11v2M1 7h2M11 7h2M2.5 2.5l1.4 1.4M10.1 10.1l1.4 1.4M2.5 11.5l1.4-1.4M10.1 3.9l1.4-1.4"
            stroke="rgba(255,255,255,.45)" strokeWidth="1" strokeLinecap="round"/>
        </svg>
      </div>
    </motion.div>
  );
}

function PlayerStatusPill({ player, confirmed, charIdx }: {
  player: 1|2; confirmed: boolean; charIdx: number;
}) {
  const accent = player === 1 ? '#A855F7' : '#3B82F6';
  const char   = CHARACTERS[charIdx];

  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 8,
      flexDirection: player === 2 ? 'row-reverse' : 'row' }}>
      <motion.div
        animate={confirmed ? {
          boxShadow: [`0 0 8px ${accent}60`, `0 0 18px ${accent}aa`, `0 0 8px ${accent}60`],
        } : {}}
        transition={{ duration: 1.5, repeat: Infinity }}
        style={{
          width: 28, height: 28, borderRadius: '50%',
          background: `linear-gradient(135deg,${accent},${char.accentB})`,
          display: 'flex', alignItems: 'center', justifyContent: 'center',
          fontSize: 10, fontWeight: 700, color: '#fff',
          fontFamily: '"Orbitron", monospace',
        }}
      >
        P{player}
      </motion.div>
      <motion.div
        animate={confirmed ? { color: accent } : {}}
        style={{
          fontSize: 10, fontWeight: 600, letterSpacing: '0.18em',
          color: `${accent}99`,
        }}
      >
        {confirmed ? 'READY' : 'SELECTING'}
      </motion.div>
    </div>
  );
}

// ─── Center divider ───────────────────────────────────────────────────────────
function Divider({ bothReady }: { bothReady: boolean }) {
  return (
    <motion.div
      animate={bothReady ? {
        boxShadow: ['0 0 6px rgba(168,85,247,.3)', '0 0 20px rgba(168,85,247,.7)', '0 0 6px rgba(168,85,247,.3)'],
      } : {}}
      transition={{ duration: 1.2, repeat: Infinity }}
      style={{
        width: 1, flexShrink: 0,
        background: 'linear-gradient(to bottom,transparent,rgba(120,160,255,.25) 20%,rgba(120,160,255,.18) 80%,transparent)',
        position: 'relative', zIndex: 5,
      }}
    />
  );
}

// ─── Player panel ─────────────────────────────────────────────────────────────
interface PlayerPanelProps {
  player:    1|2;
  state:     PlayerState;
  prevIdx:   number;
  onSelect:  (idx: number) => void;
  onConfirm: () => void;
}

function PlayerPanel({ player, state, prevIdx, onSelect, onConfirm }: PlayerPanelProps) {
  const isRight  = player === 2;
  const accent   = isRight ? '#3B82F6' : '#A855F7';
  const accentB  = isRight ? '#06B6D4' : '#EC4899';
  const char     = CHARACTERS[state.selectedIdx];
  const glowRgba = isRight ? 'rgba(59,130,246,.13)' : 'rgba(168,85,247,.13)';

  return (
    <motion.div
      initial={{ opacity: 0, x: isRight ? 40 : -40 }}
      animate={{ opacity: 1, x: 0 }}
      transition={{ duration: 0.45, delay: 0.1, ease: EASE }}
      style={{
        width: '50%', display: 'flex', flexDirection: 'column',
        position: 'relative', overflow: 'hidden',
      }}
    >
      {/* Ambient glow */}
      <div style={{
        position: 'absolute', inset: 0,
        background: `radial-gradient(ellipse at ${isRight ? '45%' : '55%'} 35%, ${glowRgba} 0%, transparent 65%)`,
        pointerEvents: 'none', zIndex: 0,
      }} />

      {/* Upper section: character preview + info */}
      <div style={{
        flex: 1, display: 'flex', flexDirection: 'column',
        padding: '14px 20px 10px',
        position: 'relative', zIndex: 1, minHeight: 0,
      }}>
        {/* Player label */}
        <div style={{
          fontSize: 9, letterSpacing: '0.28em', fontWeight: 600,
          color: `${accent}99`,
          textAlign: isRight ? 'right' : 'left',
          marginBottom: 6,
        }}>
          PLAYER {player}
        </div>

        {/* Character silhouette (animated swap) */}
        <div style={{
          flex: 1, display: 'flex', alignItems: 'center',
          justifyContent: 'center', position: 'relative', minHeight: 0,
        }}>
          <AnimatePresence mode="wait">
            <motion.div
              key={state.selectedIdx}
              initial={{ opacity: 0, y: 20, scale: 0.94 }}
              animate={{ opacity: 1, y: 0, scale: 1 }}
              exit={{ opacity: 0, y: -16, scale: 0.96 }}
              transition={{ duration: 0.28, ease: EASE }}
              style={{ position: 'absolute', display: 'flex', alignItems: 'center', justifyContent: 'center' }}
            >
              <CharacterSilhouette char={char} flip={isRight} />
            </motion.div>
          </AnimatePresence>
        </div>

        {/* Character info */}
        <AnimatePresence mode="wait">
          <motion.div
            key={`info-${state.selectedIdx}`}
            initial={{ opacity: 0, y: 10 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.22, ease: EASE }}
            style={{ marginTop: 8, textAlign: isRight ? 'right' : 'left' }}
          >
            <div style={{
              fontFamily: '"Orbitron", monospace',
              fontSize: 'clamp(24px, 3.5vw, 40px)',
              fontWeight: 900, fontStyle: 'italic',
              color: '#fff', lineHeight: 1, letterSpacing: '0.02em',
              textShadow: `0 0 22px ${char.accentH}99`,
            }}>
              {char.name}
            </div>
            <div style={{
              fontSize: 11, letterSpacing: '0.18em',
              color: `${char.accentH}aa`, marginTop: 3, fontWeight: 500,
            }}>
              {char.subtitle}
            </div>

            {/* Stat bars */}
            <div style={{
              marginTop: 12, display: 'flex',
              flexDirection: 'column', gap: 7,
            }}>
              {(Object.entries(char.stats) as [string, number][]).map(([key, val]) => (
                <StatBar
                  key={key}
                  label={key}
                  value={val}
                  accent={char.accentH}
                  flip={isRight}
                />
              ))}
            </div>
          </motion.div>
        </AnimatePresence>
      </div>

      {/* Bottom: carousel + confirm button */}
      <div style={{
        background: 'rgba(10,15,42,.72)',
        backdropFilter: 'blur(12px)',
        WebkitBackdropFilter: 'blur(12px)',
        borderTop: `1px solid ${accent}1a`,
        flexShrink: 0, zIndex: 2, position: 'relative',
      }}>
        <CharacterCarousel
          player={player}
          selectedIdx={state.selectedIdx}
          confirmed={state.confirmed}
          onSelect={onSelect}
          accent={accent}
        />

        <div style={{
          display: 'flex', justifyContent: 'center',
          padding: '6px 0 8px',
        }}>
          <ConfirmButton
            confirmed={state.confirmed}
            accent={accent}
            accentB={accentB}
            onClick={onConfirm}
          />
        </div>
      </div>
    </motion.div>
  );
}

// ─── Character silhouette (CSS/SVG) ──────────────────────────────────────────
function CharacterSilhouette({ char, flip }: { char: CharacterDef; flip: boolean }) {
  const h   = char.bodyHue;
  const col = `hsla(${h}, 72%, 78%, 0.88)`;
  const glo = `hsla(${h}, 72%, 60%, 0.5)`;

  return (
    <svg
      viewBox="0 0 130 300"
      width="130" height="285"
      fill="none"
      style={{
        filter: `drop-shadow(0 0 18px ${glo}) drop-shadow(0 0 5px ${glo})`,
        transform: flip ? 'scaleX(-1)' : 'none',
      }}
    >
      {/* Head */}
      <ellipse cx="65" cy="28" rx="19" ry="22" fill={col} opacity="0.86"/>
      {/* Neck */}
      <rect x="57" y="48" width="16" height="14" rx="4" fill={col} opacity="0.8"/>
      {/* Torso */}
      <path d="M36 62 Q33 110 35 152 L95 152 Q97 110 94 62 Q82 54 65 52 Q48 54 36 62Z"
        fill={col} opacity="0.84"/>
      {/* Left arm */}
      <path d="M36 66 Q16 78 12 112 Q10 132 16 150 Q24 155 30 143 Q26 120 36 94 Q38 78 42 68Z"
        fill={col} opacity="0.74"/>
      <path d="M16 150 Q10 166 12 190 Q16 206 30 208 Q40 206 40 194 Q32 178 32 158 Q30 148 22 146Z"
        fill={col} opacity="0.74"/>
      {/* Right arm */}
      <path d="M94 66 Q114 78 118 110 Q122 132 116 150 Q108 155 102 143 Q106 120 96 94 Q94 78 88 68Z"
        fill={col} opacity="0.74"/>
      <path d="M116 150 Q122 166 120 190 Q116 206 102 208 Q92 206 92 194 Q100 178 100 158 Q102 148 110 146Z"
        fill={col} opacity="0.74"/>
      {/* Hips */}
      <path d="M35 152 Q32 174 34 188 L96 188 Q98 174 95 152Z"
        fill={col} opacity="0.82"/>
      {/* Left leg */}
      <path d="M34 188 Q28 218 30 250 Q32 265 44 264 Q54 262 54 252 Q48 230 48 206 Q46 192 38 188Z"
        fill={col} opacity="0.8"/>
      {/* Left foot */}
      <path d="M30 250 Q24 268 28 282 Q32 290 44 288 Q54 286 54 276 Q50 264 48 252Z"
        fill={col} opacity="0.72"/>
      {/* Right leg */}
      <path d="M96 188 Q102 218 100 250 Q98 265 86 264 Q76 262 76 252 Q82 230 82 206 Q84 192 92 188Z"
        fill={col} opacity="0.8"/>
      {/* Right foot */}
      <path d="M100 250 Q106 268 102 282 Q98 290 86 288 Q76 286 76 276 Q80 264 82 252Z"
        fill={col} opacity="0.72"/>
      {/* Rim lighting */}
      <path d="M12 112 Q8 132 16 150" stroke={`hsla(${h},80%,88%,0.75)`} strokeWidth="1.5" fill="none" strokeLinecap="round"/>
      <path d="M118 110 Q124 132 116 150" stroke={`hsla(${h},80%,88%,0.55)`} strokeWidth="1.5" fill="none" strokeLinecap="round"/>
      {/* Face highlight */}
      <ellipse cx="65" cy="31" rx="9" ry="7" fill="rgba(255,255,255,.16)"/>
    </svg>
  );
}

// ─── Stat bar ──────────────────────────────────────────────────────────────────
function StatBar({ label, value, accent, flip }: {
  label: string; value: number; accent: string; flip: boolean;
}) {
  return (
    <div style={{
      display: 'flex', alignItems: 'center', gap: 8,
      flexDirection: flip ? 'row-reverse' : 'row',
    }}>
      <span style={{
        fontSize: 10, letterSpacing: '0.14em',
        color: 'rgba(255,255,255,.48)',
        minWidth: 28, textAlign: flip ? 'right' : 'left',
      }}>{label}</span>
      <div style={{
        flex: 1, height: 5, background: 'rgba(255,255,255,.08)',
        borderRadius: 3, overflow: 'hidden',
      }}>
        <motion.div
          initial={{ width: 0 }}
          animate={{ width: `${value}%` }}
          transition={{ duration: 0.45, ease: EASE, delay: 0.05 }}
          style={{
            height: '100%', borderRadius: 3,
            background: accent,
            boxShadow: `0 0 6px ${accent}66`,
          }}
        />
      </div>
      <span style={{
        fontSize: 10, color: 'rgba(255,255,255,.32)',
        minWidth: 26, textAlign: flip ? 'left' : 'right',
      }}>{value}</span>
    </div>
  );
}

// ─── Character carousel ────────────────────────────────────────────────────────
interface CarouselProps {
  player:      1|2;
  selectedIdx: number;
  confirmed:   boolean;
  onSelect:    (i: number) => void;
  accent:      string;
}

function CharacterCarousel({ player, selectedIdx, confirmed, onSelect, accent }: CarouselProps) {
  const trackRef = useRef<HTMLDivElement>(null);

  // Auto-scroll selected card into center view
  useEffect(() => {
    const track = trackRef.current;
    if (!track) return;
    const card = track.children[selectedIdx] as HTMLElement | undefined;
    if (card) {
      card.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'center' });
    }
  }, [selectedIdx]);

  return (
    <div
      ref={trackRef}
      style={{
        display: 'flex', gap: 10, overflowX: 'auto',
        scrollSnapType: 'x mandatory', padding: '10px 16px 12px',
        scrollbarWidth: 'none',
        // @ts-ignore
        WebkitScrollbar: 'none',
      }}
    >
      {CHARACTERS.map((char, i) => (
        <CarouselCard
          key={char.id}
          char={char}
          selected={i === selectedIdx}
          player={player}
          accent={accent}
          disabled={confirmed}
          onClick={() => onSelect(i)}
        />
      ))}
    </div>
  );
}

// ─── Carousel card ─────────────────────────────────────────────────────────────
interface CarouselCardProps {
  char:     CharacterDef;
  selected: boolean;
  player:   1|2;
  accent:   string;
  disabled: boolean;
  onClick:  () => void;
}

function CarouselCard({ char, selected, player, accent, disabled, onClick }: CarouselCardProps) {
  const [hovered, setHovered] = useState(false);

  return (
    <motion.div
      onClick={disabled ? undefined : onClick}
      onHoverStart={() => setHovered(true)}
      onHoverEnd={() => setHovered(false)}
      animate={{
        scale:      selected ? 1.13 : hovered ? 1.07 : 1,
        borderColor: selected ? accent : hovered ? `${accent}55` : 'rgba(255,255,255,.09)',
        boxShadow:   selected
          ? `0 0 22px ${accent}88, 0 0 8px ${char.accentB}44`
          : hovered
          ? `0 0 12px ${accent}44`
          : 'none',
      }}
      transition={{ duration: 0.2, ease: EASE }}
      style={{
        flexShrink: 0, width: 82, height: 108,
        borderRadius: 10, cursor: disabled ? 'not-allowed' : 'pointer',
        scrollSnapAlign: 'center',
        background: `linear-gradient(160deg,hsl(${char.bodyHue},40%,10%),hsl(${char.bodyHue},50%,16%))`,
        border: '1.5px solid rgba(255,255,255,.09)',
        position: 'relative', overflow: 'hidden',
        opacity: disabled && !selected ? 0.5 : 1,
      }}
    >
      {/* Mini silhouette */}
      <MiniSilhouette char={char} />

      {/* Name label */}
      <div style={{
        position: 'absolute', bottom: 0, left: 0, right: 0,
        padding: '5px 6px',
        background: 'linear-gradient(to top,rgba(0,0,0,.72),transparent)',
      }}>
        <div style={{
          fontFamily: '"Orbitron", monospace',
          fontSize: 7.5, fontWeight: 700,
          color: char.accentH, letterSpacing: '0.08em',
          whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis',
        }}>
          {char.name}
        </div>
      </div>
    </motion.div>
  );
}

// ─── Mini silhouette for carousel card ────────────────────────────────────────
function MiniSilhouette({ char }: { char: CharacterDef }) {
  const col = `hsla(${char.bodyHue}, 68%, 76%, 0.82)`;
  return (
    <svg viewBox="0 0 60 96" width="48" height="78"
      fill="none"
      style={{ margin: '8px auto 0', display: 'block',
        filter: `drop-shadow(0 0 5px hsla(${char.bodyHue},70%,60%,.5))` }}>
      <ellipse cx="30" cy="13" rx="9" ry="10" fill={col} opacity=".82"/>
      <path d="M16 24 Q14 50 15 65 L45 65 Q46 50 44 24 Q38 20 30 20 Q22 20 16 24Z"
        fill={col} opacity=".78"/>
      <path d="M15 26 Q6 32 5 48 Q4 58 8 65 Q12 67 15 62 Q13 50 17 38Z"
        fill={col} opacity=".65"/>
      <path d="M45 26 Q54 32 55 48 Q56 58 52 65 Q48 67 45 62 Q47 50 43 38Z"
        fill={col} opacity=".65"/>
      <path d="M15 65 Q13 78 14 90 Q16 95 22 94 Q27 93 27 88 Q24 80 24 70Z"
        fill={col} opacity=".72"/>
      <path d="M45 65 Q47 78 46 90 Q44 95 38 94 Q33 93 33 88 Q36 80 36 70Z"
        fill={col} opacity=".72"/>
    </svg>
  );
}

// ─── Confirm button ───────────────────────────────────────────────────────────
function ConfirmButton({ confirmed, accent, accentB, onClick }: {
  confirmed: boolean; accent: string; accentB: string; onClick: () => void;
}) {
  return (
    <motion.button
      onClick={confirmed ? undefined : onClick}
      whileHover={confirmed ? {} : { scale: 1.04 }}
      whileTap={confirmed ? {} : { scale: 0.97 }}
      animate={confirmed ? {
        boxShadow: [`0 0 10px ${accent}60`, `0 0 22px ${accent}aa`, `0 0 10px ${accent}60`],
        borderColor: [`${accent}88`, accent, `${accent}88`],
      } : {}}
      transition={confirmed ? { duration: 1.5, repeat: Infinity } : {}}
      style={{
        background: confirmed
          ? `linear-gradient(135deg,${accent}28,${accentB}18)`
          : `${accent}18`,
        border: `1.5px solid ${confirmed ? accent : `${accent}55`}`,
        color: confirmed ? '#fff' : `${accent}dd`,
        padding: '8px 28px',
        borderRadius: 100,
        fontSize: 11, fontWeight: 600,
        letterSpacing: '0.22em',
        cursor: confirmed ? 'default' : 'pointer',
        fontFamily: '"Rajdhani", monospace',
        outline: 'none',
      }}
    >
      {confirmed ? 'READY' : 'CONFIRM SELECTION'}
    </motion.button>
  );
}