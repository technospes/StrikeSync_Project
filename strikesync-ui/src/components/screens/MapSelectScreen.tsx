// src/components/screens/MapSelectScreen.tsx
//
// Cinematic map randomizer — casino-roll slot machine effect.
// Sends flat-protocol { type:"map_selected", map:<id>, mapName:"<n>" } to Unity.
// Unity loads the selected map and fires state_change: countdown.

import {
  useState, useEffect, useRef, useCallback,
} from 'react';
import { motion, AnimatePresence } from 'framer-motion'; // useAnimation intentionally omitted
import { sendWS } from '../../hooks/useWebSocket';

// ─── Map pool ────────────────────────────────────────────────────────────────
export interface MapDef {
  id:          number;
  name:        string;
  subtitle:    string;
  location:    string;
  accentH:     string;
  accentB:     string;
  bgHue:       number;
  stats: {
    visibility: string;
    gravity:    string;
    danger:     string;
  };
  description: string;
}

export const MAPS: MapDef[] = [
  {
    id: 0, name: 'IRON WASTELAND', subtitle: 'THE RUST ZONE',
    location: 'SECTOR 7-G // INDUSTRIAL DISTRICT',
    accentH: '#EF4444', accentB: '#F97316', bgHue: 15,
    stats: { visibility: 'CRITICAL', gravity: '1.02 G', danger: 'MAXIMUM' },
    description: 'Collapsed factory corridors. Exposed girders. No escape routes.',
  },
  {
    id: 1, name: 'NEON DOJO', subtitle: 'THE SACRED RING',
    location: 'TOKYO UNDERGROUND // LEVEL B3',
    accentH: '#A855F7', accentB: '#EC4899', bgHue: 280,
    stats: { visibility: 'MODERATE', gravity: '0.98 G', danger: 'HIGH' },
    description: 'Ancient training hall flooded with neon light. Honor demands blood.',
  },
  {
    id: 2, name: 'THE VOID', subtitle: 'NULL SPACE',
    location: 'COORDINATES: UNKNOWN',
    accentH: '#06B6D4', accentB: '#3B82F6', bgHue: 210,
    stats: { visibility: 'ZERO', gravity: '0.00 G', danger: 'MAXIMUM' },
    description: 'Featureless infinite dark. Only the fighters and the fight.',
  },
  {
    id: 3, name: 'CRIMSON PEAK', subtitle: 'THE HIGH GROUND',
    location: 'MOUNTAIN FACILITY // ELEVATION 4,200M',
    accentH: '#DC2626', accentB: '#7C3AED', bgHue: 340,
    stats: { visibility: 'CLEAR', gravity: '0.94 G', danger: 'HIGH' },
    description: 'Exposed cliffside platform. Thin air. No safety rails.',
  },
  {
    id: 4, name: 'REACTOR CORE', subtitle: 'MELTDOWN ZONE',
    location: 'NUCLEAR PLANT Ω-9 // SUBLEVEL 12',
    accentH: '#10B981', accentB: '#34D399', bgHue: 155,
    stats: { visibility: 'OBSCURED', gravity: '1.05 G', danger: 'MAXIMUM' },
    description: 'Pulsing radiation. The floor is alive. So is the ceiling.',
  },
  {
    id: 5, name: 'STORM BARGE', subtitle: 'OPEN SEA ARENA',
    location: 'NORTH ATLANTIC // STORM SYSTEM DELTA',
    accentH: '#F59E0B', accentB: '#EF4444', bgHue: 40,
    stats: { visibility: 'POOR', gravity: '1.00 G', danger: 'MEDIUM' },
    description: 'Heaving deck. Crashing waves. The ocean picks a winner too.',
  },
];

// ─── Constants ────────────────────────────────────────────────────────────────
const EASE_CINEMATIC: [number, number, number, number] = [0.22, 1, 0.36, 1];
const SPIN_DURATION_MS  = 3800;
const DECEL_DURATION_MS = 1200;
const SEND_DELAY_MS     = 2200;

// ─── Helpers ──────────────────────────────────────────────────────────────────
function pickRandom<T>(arr: T[]): T {
  return arr[Math.floor(Math.random() * arr.length)];
}

function getDangerColor(danger: string): string {
  if (danger.includes('MAXIMUM'))  return '#ff0000';
  if (danger.includes('CRITICAL')) return '#dc2626';
  if (danger.includes('HIGH'))     return '#ef4444';
  if (danger.includes('MEDIUM'))   return '#f59e0b';
  return '#22c55e';
}

// ─── Root ─────────────────────────────────────────────────────────────────────
export function MapSelectScreen() {
  const [phase, setPhase] = useState<
    'intro' | 'spinning' | 'decelerating' | 'locked' | 'entering'
  >('intro');

  const [displayIdx,  setDisplayIdx]  = useState(0);
  const [selectedMap, setSelectedMap] = useState<MapDef | null>(null);
  const [scanLine,    setScanLine]    = useState(0);

  const spinIntervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const timeoutRefs     = useRef<ReturnType<typeof setTimeout>[]>([]);

  const addTimeout = useCallback((fn: () => void, ms: number) => {
    const id = setTimeout(fn, ms);
    timeoutRefs.current.push(id);
  }, []);

  // ── Spin sequence ─────────────────────────────────────────────────────────
  const startSpin = useCallback(() => {
    setPhase('spinning');
    const finalMap = pickRandom(MAPS);
    let speed      = 80;
    let elapsed    = 0;
    let idx        = 0;

    const fastSpin = setInterval(() => {
      elapsed += speed;
      idx = (idx + 1) % MAPS.length;
      setDisplayIdx(idx);

      if (elapsed >= SPIN_DURATION_MS - DECEL_DURATION_MS) {
        clearInterval(fastSpin);
        setPhase('decelerating');

        let slowSpeed = 90;
        const slowSpin = () => {
          idx = (idx + 1) % MAPS.length;
          setDisplayIdx(idx);
          slowSpeed = Math.min(slowSpeed * 1.35, 600);

          if (MAPS[idx].id === finalMap.id && slowSpeed > 350) {
            setPhase('locked');
            setSelectedMap(finalMap);
            addTimeout(() => {
              setPhase('entering');
              // FLAT PROTOCOL — no nested value object
              sendWS('map_selected', { map: finalMap.id, mapName: finalMap.name });
            }, SEND_DELAY_MS);
            return;
          }
          addTimeout(slowSpin, slowSpeed);
        };
        addTimeout(slowSpin, slowSpeed);
      }
    }, speed);
    spinIntervalRef.current = fastSpin;
  }, [addTimeout]);

  // ── Scan line ─────────────────────────────────────────────────────────────
  useEffect(() => {
    const id = setInterval(() => setScanLine(n => (n + 1) % 100), 20);
    return () => clearInterval(id);
  }, []);

  // ── Kick off spin ─────────────────────────────────────────────────────────
  // FIX: capture ref contents in local variables before returning the cleanup
  // function — avoids the "ref value will have changed" exhaustive-deps warning.
  useEffect(() => {
    addTimeout(() => startSpin(), 1400);
    const capturedTimeouts = timeoutRefs.current;
    const capturedInterval = spinIntervalRef;
    return () => {
      capturedTimeouts.forEach(clearTimeout);
      if (capturedInterval.current) clearInterval(capturedInterval.current);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const currentMap = MAPS[displayIdx];
  const lockedMap  = selectedMap ?? currentMap;
  const isLocked   = phase === 'locked' || phase === 'entering';
  const isSpinning = phase === 'spinning' || phase === 'decelerating';

  return (
    <motion.div
      initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
      transition={{ duration: 0.4 }}
      style={{
        position: 'fixed', inset: 0, background: '#050A18',
        fontFamily: '"Rajdhani", monospace', overflow: 'hidden',
        display: 'flex', flexDirection: 'column',
      }}
    >
      {/* Background glow */}
      <AnimatePresence mode="wait">
        <motion.div
          key={isLocked ? lockedMap.id : 'spin'}
          initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
          transition={{ duration: 0.6 }}
          style={{
            position: 'absolute', inset: 0,
            background: isLocked
              ? `radial-gradient(ellipse at 50% 40%,${lockedMap.accentH}22 0%,transparent 75%)`
              : `radial-gradient(ellipse at 50% 40%,${currentMap.accentH}15 0%,transparent 70%)`,
            pointerEvents: 'none', zIndex: 0,
          }}
        />
      </AnimatePresence>

      {/* Diagonal texture */}
      <div style={{
        position: 'absolute', inset: 0,
        backgroundImage: 'repeating-linear-gradient(135deg,transparent,transparent 38px,rgba(168,85,247,.015) 38px,rgba(168,85,247,.015) 39px)',
        pointerEvents: 'none', zIndex: 0,
      }} />

      {/* Scan line */}
      <div style={{
        position: 'absolute', left: 0, right: 0, height: 1,
        top: `${scanLine}%`, background: 'rgba(168,85,247,0.06)',
        pointerEvents: 'none', zIndex: 1,
      }} />

      <TopBar phase={phase} />

      <div style={{
        flex: 1, display: 'flex', flexDirection: 'column',
        alignItems: 'center', justifyContent: 'center',
        position: 'relative', zIndex: 2, padding: '0 40px',
      }}>
        <TitleBlock phase={phase} />

        <div style={{ width: '100%', maxWidth: 580, position: 'relative' }}>
          <SideCard map={MAPS[(displayIdx - 1 + MAPS.length) % MAPS.length]} side="left"  visible={isSpinning} />
          <SideCard map={MAPS[(displayIdx + 1) % MAPS.length]}               side="right" visible={isSpinning} />

          <AnimatePresence mode="wait">
            <CenterCard
              key={isLocked ? `locked-${lockedMap.id}` : displayIdx}
              map={isLocked ? lockedMap : currentMap}
              locked={isLocked}
              spinning={isSpinning}
            />
          </AnimatePresence>

          <AnimatePresence>
            {isSpinning && <SpinBand key="spinband" />}
          </AnimatePresence>
        </div>

        <StatusLabel phase={phase} />
      </div>

      <Footer phase={phase} lockedMap={isLocked ? lockedMap : null} />

      <AnimatePresence>
        {phase === 'entering' && selectedMap && (
          <EnteringOverlay map={selectedMap} />
        )}
      </AnimatePresence>
    </motion.div>
  );
}

// ─── TopBar ──────────────────────────────────────────────────────────────────
function TopBar({ phase }: { phase: string }) {
  return (
    <motion.div
      initial={{ y: -40, opacity: 0 }} animate={{ y: 0, opacity: 1 }}
      transition={{ duration: 0.4, ease: EASE_CINEMATIC }}
      style={{
        display: 'flex', alignItems: 'center', justifyContent: 'space-between',
        padding: '0 28px', height: 48,
        background: 'rgba(5,10,24,.9)',
        borderBottom: '1px solid rgba(255,255,255,.05)',
        flexShrink: 0, zIndex: 10, position: 'relative',
      }}
    >
      <span style={{
        fontFamily: '"Orbitron", monospace', fontSize: 13,
        fontWeight: 900, fontStyle: 'italic',
        background: 'linear-gradient(135deg,#C084FC,#EC4899)',
        WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent',
        backgroundClip: 'text', letterSpacing: '0.04em',
      }}>STRIKESYNC</span>

      <div style={{ fontSize: 9, letterSpacing: '0.28em', fontWeight: 600, color: 'rgba(168,130,220,.6)' }}>
        {phase === 'intro' || phase === 'spinning' || phase === 'decelerating'
          ? 'SYSTEM PROTOCOL: ARENA RANDOMIZATION'
          : 'ARENA SELECTION COMPLETE'}
      </div>

      <div style={{ display: 'flex', gap: 6 }}>
        {[0, 1, 2].map(i => (
          <motion.span
            key={i}
            animate={{ opacity: [0.2, 1, 0.2] }}
            transition={{ duration: 1.2, repeat: Infinity, delay: i * 0.3 }}
            style={{ fontSize: 6, color: '#A855F7' }}
          >●</motion.span>
        ))}
      </div>
    </motion.div>
  );
}

// ─── TitleBlock ───────────────────────────────────────────────────────────────
function TitleBlock({ phase }: { phase: string }) {
  const label =
    phase === 'intro'        ? 'INITIALIZING ARENA SELECTION...' :
    phase === 'spinning'     ? 'SCANNING AVAILABLE ARENAS...'    :
    phase === 'decelerating' ? 'LOCKING TARGET...'               :
    phase === 'locked'       ? 'ARENA CONFIRMED'                 :
                               'LOADING ARENA';

  return (
    <motion.div
      initial={{ opacity: 0, y: -20 }} animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.5, delay: 0.2, ease: EASE_CINEMATIC }}
      style={{ textAlign: 'center', marginBottom: 32 }}
    >
      <div style={{
        fontSize: 11, letterSpacing: '0.32em', fontWeight: 600,
        color: 'rgba(168,130,220,.55)', marginBottom: 10,
      }}>
        {label}
      </div>
      <motion.h1
        animate={phase === 'locked' || phase === 'entering' ? {
          textShadow: [
            '0 0 20px rgba(168,85,247,.4)',
            '0 0 50px rgba(168,85,247,.8)',
            '0 0 20px rgba(168,85,247,.4)',
          ],
        } : {}}
        transition={{ duration: 2, repeat: Infinity }}
        style={{
          margin: 0, fontFamily: '"Orbitron", monospace',
          fontSize: 'clamp(28px, 4.5vw, 52px)',
          fontWeight: 900, fontStyle: 'italic', letterSpacing: '0.06em',
          background: 'linear-gradient(135deg,#C084FC 0%,#A855F7 40%,#EC4899 100%)',
          WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent',
          backgroundClip: 'text',
        }}
      >
        SELECTING ARENA
      </motion.h1>
    </motion.div>
  );
}

// ─── CenterCard ───────────────────────────────────────────────────────────────
function CenterCard({ map, locked, spinning }: {
  map: MapDef; locked: boolean; spinning: boolean;
}) {
  const g = map.accentH;
  return (
    <motion.div
      initial={{ scale: spinning ? 0.97 : 0.88, opacity: spinning ? 0.7 : 0 }}
      animate={{
        scale: 1, opacity: 1,
        boxShadow: locked
          ? [`0 0 30px ${g}50,0 0 80px ${g}25`, `0 0 60px ${g}80,0 0 120px ${g}40`, `0 0 30px ${g}50,0 0 80px ${g}25`]
          : `0 0 20px ${g}30`,
      }}
      exit={{ scale: 0.96, opacity: 0 }}
      transition={
        spinning ? { duration: 0.08 }
        : locked  ? { scale: { duration: 0.5, ease: EASE_CINEMATIC }, opacity: { duration: 0.4 }, boxShadow: { duration: 2.5, repeat: Infinity } }
        : { duration: 0.4, ease: EASE_CINEMATIC }
      }
      style={{
        width: '100%', borderRadius: 16,
        border: `2px solid ${locked ? g : 'rgba(168,85,247,.25)'}`,
        background: `linear-gradient(160deg,hsl(${map.bgHue},35%,7%),hsl(${map.bgHue},45%,12%))`,
        overflow: 'hidden', position: 'relative', minHeight: 280,
      }}
    >
      <CornerBrackets color={g} locked={locked} />
      <ProceduralMapBg map={map} spinning={spinning} />

      {/* Scan lines */}
      <div style={{
        position: 'absolute', inset: 0,
        backgroundImage: 'repeating-linear-gradient(0deg,transparent,transparent 3px,rgba(0,0,0,.04) 3px,rgba(0,0,0,.04) 4px)',
        pointerEvents: 'none',
      }} />

      <div style={{ position: 'relative', zIndex: 2, padding: '24px 28px', display: 'flex', flexDirection: 'column', minHeight: 280 }}>
        <div style={{ fontSize: 9, letterSpacing: '0.28em', fontWeight: 600, color: locked ? g : 'rgba(255,255,255,.4)', marginBottom: 8, transition: 'color 0.4s' }}>
          {locked ? 'LOCATION LOCKED' : 'SCANNING...'}
        </div>

        <motion.div
          animate={locked ? { scale: [1, 1.02, 1] } : {}}
          transition={{ duration: 1.5, repeat: Infinity }}
          style={{
            fontFamily: '"Orbitron", monospace',
            fontSize: 'clamp(26px, 4vw, 42px)', fontWeight: 900, fontStyle: 'italic',
            color: '#fff', lineHeight: 1, letterSpacing: '0.02em',
            textShadow: locked ? `0 0 24px ${g}cc` : 'none',
            marginBottom: 4, transition: 'text-shadow 0.4s',
          }}
        >
          {map.name}
        </motion.div>

        <div style={{ fontSize: 11, letterSpacing: '0.18em', color: `${g}cc`, marginBottom: 6, fontWeight: 500 }}>
          {map.subtitle}
        </div>
        <div style={{ fontSize: 9, letterSpacing: '0.2em', color: 'rgba(255,255,255,.3)', marginBottom: 20 }}>
          {map.location}
        </div>

        <AnimatePresence>
          {locked && (
            <motion.div
              initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: 'auto' }}
              transition={{ duration: 0.4, delay: 0.2 }}
              style={{ fontSize: 12, color: 'rgba(255,255,255,.55)', letterSpacing: '0.06em', lineHeight: 1.6, marginBottom: 20 }}
            >
              {map.description}
            </motion.div>
          )}
        </AnimatePresence>

        <div style={{ display: 'flex', borderTop: '1px solid rgba(255,255,255,.08)', paddingTop: 16, marginTop: 'auto' }}>
          {(Object.entries(map.stats) as [string, string][]).map(([key, val]) => (
            <StatCell key={key} label={key} value={val} accent={g} locked={locked} />
          ))}
        </div>
      </div>
    </motion.div>
  );
}

// ─── ProceduralMapBg ──────────────────────────────────────────────────────────
function ProceduralMapBg({ map, spinning }: { map: MapDef; spinning: boolean }) {
  const h = map.bgHue;
  return (
    <div style={{
      position: 'absolute', inset: 0,
      background: `radial-gradient(ellipse at 80% 20%,hsla(${h},70%,50%,.18) 0%,transparent 50%),radial-gradient(ellipse at 20% 80%,hsla(${h + 40},60%,40%,.12) 0%,transparent 45%),radial-gradient(ellipse at 50% 50%,hsla(${h},50%,20%,.3) 0%,transparent 70%)`,
      opacity: spinning ? 0.5 : 1, transition: 'opacity 0.6s',
    }}>
      <svg width="100%" height="100%" style={{ position: 'absolute', inset: 0 }}>
        <defs>
          <pattern id={`grid-${map.id}`} width="40" height="40" patternUnits="userSpaceOnUse">
            <path d="M 40 0 L 0 0 0 40" fill="none" stroke={`hsla(${h},60%,60%,0.06)`} strokeWidth="0.5"/>
          </pattern>
        </defs>
        <rect width="100%" height="100%" fill={`url(#grid-${map.id})`}/>
      </svg>
    </div>
  );
}

// ─── CornerBrackets ───────────────────────────────────────────────────────────
// FIX: removed unused `corners` array — paths written directly in JSX
function CornerBrackets({ color, locked }: { color: string; locked: boolean }) {
  const op  = locked ? 0.8 : 0.35;
  const sw  = locked ? 2 : 1.5;
  const len = 20;
  const o   = 12; // offset from edge
  const f   = (n: number) => `calc(100% - ${n}px)`; // shorthand for "100% minus N px"

  const pathProps = { fill: 'none', stroke: color, strokeWidth: sw, strokeLinecap: 'round' as const };
  const motionProps = { initial: { opacity: 0.2 }, animate: { opacity: op }, transition: { duration: 0.4 } };

  return (
    <svg style={{ position: 'absolute', inset: 0, width: '100%', height: '100%', pointerEvents: 'none', zIndex: 3, overflow: 'visible' }}>
      <motion.path d={`M${o},${o + len} L${o},${o} L${o + len},${o}`} {...pathProps} {...motionProps} />
      <motion.path d={`M${f(o + len)},${o} L${f(o)},${o} L${f(o)},${o + len}`} {...pathProps} {...motionProps} />
      <motion.path d={`M${o},${f(o + len)} L${o},${f(o)} L${o + len},${f(o)}`} {...pathProps} {...motionProps} />
      <motion.path d={`M${f(o + len)},${f(o)} L${f(o)},${f(o)} L${f(o)},${f(o + len)}`} {...pathProps} {...motionProps} />
    </svg>
  );
}

// ─── StatCell ────────────────────────────────────────────────────────────────
function StatCell({ label, value, accent, locked }: { label: string; value: string; accent: string; locked: boolean }) {
  const color = label === 'danger' ? getDangerColor(value) : accent;
  return (
    <div style={{ flex: 1, textAlign: 'center', borderRight: '1px solid rgba(255,255,255,.06)', padding: '0 12px' }}>
      <div style={{ fontSize: 8, letterSpacing: '0.2em', color: 'rgba(255,255,255,.35)', marginBottom: 5, textTransform: 'uppercase' }}>
        {label}
      </div>
      <motion.div
        animate={locked && label === 'danger' ? { color: [color, '#fff', color] } : {}}
        transition={{ duration: 1.8, repeat: Infinity }}
        style={{ fontSize: 11, fontWeight: 700, letterSpacing: '0.1em', color: locked ? color : 'rgba(255,255,255,.5)', transition: 'color 0.4s' }}
      >
        {value}
      </motion.div>
    </div>
  );
}

// ─── SideCard ────────────────────────────────────────────────────────────────
function SideCard({ map, side, visible }: { map: MapDef; side: 'left' | 'right'; visible: boolean }) {
  return (
    <AnimatePresence>
      {visible && (
        <motion.div
          initial={{ opacity: 0, x: side === 'left' ? -20 : 20 }}
          animate={{ opacity: 1, x: 0 }}
          exit={{ opacity: 0 }}
          style={{
            position: 'absolute',
            left:  side === 'left'  ? 'calc(-32% - 12px)' : undefined,
            right: side === 'right' ? 'calc(-32% - 12px)' : undefined,
            top: '10%', bottom: '10%', width: '30%',
            borderRadius: 12, border: '1px solid rgba(255,255,255,.08)',
            background: `linear-gradient(160deg,hsl(${map.bgHue},25%,6%),hsl(${map.bgHue},30%,10%))`,
            opacity: 0.45, overflow: 'hidden',
            display: 'flex', flexDirection: 'column', padding: '14px 12px', gap: 4,
          }}
        >
          <div style={{ fontSize: 8, letterSpacing: '0.2em', color: 'rgba(255,255,255,.3)' }}>
            {side === 'left' ? '◀ PREV' : 'NEXT ▶'}
          </div>
          <div style={{ fontFamily: '"Orbitron", monospace', fontSize: 11, fontWeight: 900, fontStyle: 'italic', color: 'rgba(255,255,255,.55)', lineHeight: 1.2 }}>
            {map.name}
          </div>
          <div style={{ fontSize: 9, color: 'rgba(255,255,255,.25)' }}>{map.subtitle}</div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}

// ─── SpinBand ────────────────────────────────────────────────────────────────
function SpinBand() {
  return (
    <motion.div
      initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
      style={{ position: 'absolute', top: -1, left: -1, right: -1, bottom: -1, border: '2px solid rgba(168,85,247,.4)', borderRadius: 18, pointerEvents: 'none' }}
    >
      <motion.div
        animate={{ x: ['0%', '100%', '0%'] }}
        transition={{ duration: 0.8, repeat: Infinity, ease: 'linear' }}
        style={{ position: 'absolute', top: -1, height: 2, width: '30%', background: 'linear-gradient(90deg,transparent,rgba(168,85,247,.8),transparent)' }}
      />
    </motion.div>
  );
}

// ─── StatusLabel ──────────────────────────────────────────────────────────────
function StatusLabel({ phase }: { phase: string }) {
  return (
    <motion.div
      initial={{ opacity: 0 }} animate={{ opacity: 1 }} transition={{ delay: 0.4 }}
      style={{ marginTop: 22, textAlign: 'center', height: 40, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 6 }}
    >
      {(phase === 'spinning' || phase === 'decelerating') && (
        <div style={{ display: 'flex', gap: 6 }}>
          {[0, 1, 2, 3, 4].map(i => (
            <motion.div
              key={i}
              animate={{ scaleY: [0.4, 1, 0.4] }}
              transition={{ duration: 0.5, repeat: Infinity, delay: i * 0.1 }}
              style={{ width: 3, height: 18, background: '#A855F7', borderRadius: 2, transformOrigin: 'center' }}
            />
          ))}
        </div>
      )}
      {phase === 'locked' && (
        <motion.div
          initial={{ scale: 0.8, opacity: 0 }} animate={{ scale: 1, opacity: 1 }}
          transition={{ duration: 0.4, ease: EASE_CINEMATIC }}
          style={{
            fontFamily: '"Orbitron", monospace', fontSize: 13, fontWeight: 700, letterSpacing: '0.2em',
            background: 'linear-gradient(135deg,#A855F7,#EC4899)',
            WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent', backgroundClip: 'text',
          }}
        >
          ARENA LOCKED — PREPARING BATTLEFIELD
        </motion.div>
      )}
    </motion.div>
  );
}

// ─── Footer ───────────────────────────────────────────────────────────────────
function Footer({ phase, lockedMap }: { phase: string; lockedMap: MapDef | null }) {
  return (
    <motion.div
      initial={{ y: 20, opacity: 0 }} animate={{ y: 0, opacity: 1 }} transition={{ duration: 0.5, delay: 0.3 }}
      style={{
        height: 52, background: 'rgba(5,10,24,.85)', backdropFilter: 'blur(8px)', WebkitBackdropFilter: 'blur(8px)',
        borderTop: '1px solid rgba(255,255,255,.05)',
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        flexShrink: 0, zIndex: 10, position: 'relative', gap: 32,
      }}
    >
      {lockedMap ? (
        <>
          <ControlItem icon="◆" label="ARENA HAZARDS ACTIVE" active />
          <ControlItem icon="●" label={`GRAVITY: ${lockedMap.stats.gravity}`} active />
          <ControlItem icon="▲" label={`DANGER: ${lockedMap.stats.danger}`} active danger />
        </>
      ) : (
        <>
          <ControlItem icon="◀" label="PREV" />
          <ControlItem icon="●" label="RANDOMIZING" active />
          <ControlItem icon="▶" label="NEXT" />
        </>
      )}
    </motion.div>
  );
}

function ControlItem({ icon, label, active, danger }: { icon: string; label: string; active?: boolean; danger?: boolean }) {
  return (
    <div style={{
      display: 'flex', alignItems: 'center', gap: 8, fontSize: 9, letterSpacing: '0.18em',
      color: danger ? '#ef4444' : active ? 'rgba(168,130,220,.8)' : 'rgba(255,255,255,.25)',
    }}>
      <span style={{ fontSize: 7, opacity: 0.7 }}>{icon}</span>
      <span>{label}</span>
    </div>
  );
}

// ─── EnteringOverlay ─────────────────────────────────────────────────────────
function EnteringOverlay({ map }: { map: MapDef }) {
  return (
    <motion.div
      initial={{ opacity: 0 }} animate={{ opacity: 1 }}
      style={{
        position: 'absolute', inset: 0, zIndex: 200,
        background: `radial-gradient(ellipse at center,${map.accentH}15 0%,#050A18 70%)`,
        display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 20,
      }}
    >
      {/* Expanding ring */}
      <motion.div
        initial={{ scale: 0.5, opacity: 0.8 }} animate={{ scale: 3, opacity: 0 }}
        transition={{ duration: 1.8, ease: 'easeOut' }}
        style={{ position: 'absolute', width: 200, height: 200, borderRadius: '50%', border: `2px solid ${map.accentH}` }}
      />

      <motion.div
        initial={{ scale: 0.7, opacity: 0 }} animate={{ scale: 1, opacity: 1 }}
        transition={{ delay: 0.3, duration: 0.6, ease: EASE_CINEMATIC }}
        style={{ fontFamily: '"Orbitron", monospace', fontSize: 'clamp(14px,2.5vw,22px)', fontWeight: 700, letterSpacing: '0.28em', color: map.accentH }}
      >
        ENTERING ARENA
      </motion.div>

      <motion.div
        initial={{ scale: 0.6, opacity: 0 }} animate={{ scale: 1, opacity: 1 }}
        transition={{ delay: 0.5, duration: 0.5, ease: EASE_CINEMATIC }}
        style={{ fontFamily: '"Orbitron", monospace', fontSize: 'clamp(28px,5vw,56px)', fontWeight: 900, fontStyle: 'italic', color: '#fff', letterSpacing: '0.04em', textShadow: `0 0 40px ${map.accentH}` }}
      >
        {map.name}
      </motion.div>

      {/* Loading bar */}
      <motion.div style={{ width: 280, height: 2, background: 'rgba(255,255,255,.1)', borderRadius: 1, overflow: 'hidden', marginTop: 12 }}>
        <motion.div
          initial={{ width: 0 }} animate={{ width: '100%' }}
          transition={{ delay: 0.6, duration: 1.4, ease: 'easeInOut' }}
          style={{ height: '100%', background: map.accentH, borderRadius: 1 }}
        />
      </motion.div>
    </motion.div>
  );
}