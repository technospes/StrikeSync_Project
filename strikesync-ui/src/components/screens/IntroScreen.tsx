// src/components/screens/IntroScreen.tsx
// Matches the reference image: cyberpunk splash screen with
// animated title, character silhouette, glass panel, and cinematic entry.

import { useEffect, useRef, useState, useCallback } from 'react';
import { motion, AnimatePresence, useAnimation } from 'framer-motion';
import { sendWS } from '../../hooks/useWebSocket';

// ─── Types ─────────────────────────────────────────────────────────────────
interface IntroScreenProps {
  wsConnected:    boolean;
  unityConnected: boolean;
}

// ─── Constants ─────────────────────────────────────────────────────────────
const EASE_CINEMATIC: [number, number, number, number] = [0.22, 1, 0.36, 1];

// ─── Main Component ────────────────────────────────────────────────────────
export function IntroScreen({ wsConnected, unityConnected }: IntroScreenProps) {
  const [phase, setPhase]           = useState<'entering' | 'idle' | 'exiting'>('entering');
  const [activeDot, setActiveDot]   = useState(0);
  const [glowBurst, setGlowBurst]   = useState(false);
  const titleControls               = useAnimation();
  const containerRef                = useRef<HTMLDivElement>(null);

  // ── Entry sequence ───────────────────────────────────────────────────────
  useEffect(() => {
    const run = async () => {
      await titleControls.start('visible');
      setPhase('idle');
    };
    const t = setTimeout(run, 200);
    return () => clearTimeout(t);
  }, [titleControls]);

  // ── Dot cycling ─────────────────────────────────────────────────────────
  useEffect(() => {
    const id = setInterval(() => setActiveDot(d => (d + 1) % 3), 600);
    return () => clearInterval(id);
  }, []);

  // ── Key/click handler ────────────────────────────────────────────────────
  const handleStart = useCallback(async () => {
    if (phase !== 'idle') return;
    setPhase('exiting');
    setGlowBurst(true);
    setTimeout(() => setGlowBurst(false), 400);
    sendWS('goto_char_select');
  }, [phase]);

  useEffect(() => {
    const onKey = () => handleStart();
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [handleStart]);

  const connectionLabel = !wsConnected
    ? 'DISCONNECTED // BRIDGE_OFFLINE'
    : !unityConnected
    ? 'BRIDGE OK // WAITING FOR UNITY'
    : 'SYNCED // DATACENTER_EAST_01';

  const connectionColor = !wsConnected ? '#ef4444' : !unityConnected ? '#f59e0b' : '#22c55e';

  return (
    <motion.div
      ref={containerRef}
      onClick={handleStart}
      initial={{ opacity: 0 }}
      animate={{ opacity: phase === 'exiting' ? 0 : 1 }}
      transition={{ duration: phase === 'exiting' ? 0.7 : 0.3, ease: EASE_CINEMATIC }}
      style={styles.root}
    >
      {/* ── Layered background ────────────────────────────────────────── */}
      <Background glowBurst={glowBurst} />

      {/* ── Left bracket decoration (top-left corner) ─────────────────── */}
      <CornerBracket />

      {/* ── Glass panel ───────────────────────────────────────────────── */}
      <motion.div
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        transition={{ duration: 0.5, delay: 0.1 }}
        style={styles.glassPanel}
      >
        {/* Diagonal scan lines texture */}
        <div style={styles.scanLines} />
        <div style={styles.diagonalLines} />

        {/* ── Character silhouette ─────────────────────────────────── */}
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          transition={{ duration: 0.8, delay: 0.3, ease: EASE_CINEMATIC }}
          style={styles.silhouetteWrap}
        >
          <CharacterSilhouette />
        </motion.div>

        {/* ── Center content stack ─────────────────────────────────── */}
        <div style={styles.centerStack}>

          {/* Subtitle above title */}
          <motion.div
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.5, delay: 0.5 }}
            style={styles.subtitle}
          >
            NEURAL COMBAT PROTOCOL V.2.4.0
          </motion.div>

          {/* STRIKESYNC title */}
          <motion.div
            variants={{
              hidden:  { scale: 0.82, opacity: 0 },
              visible: { scale: 1,    opacity: 1 },
            }}
            initial="hidden"
            animate={titleControls}
            transition={{ duration: 0.7, delay: 0.3, ease: EASE_CINEMATIC }}
            style={styles.titleWrap}
          >
            {/* Glow layers (paint behind the text) */}
            <div style={{ ...styles.titleGlow, opacity: glowBurst ? 1 : 0.55,
              filter: `blur(${glowBurst ? 50 : 32}px)`,
              transition: 'opacity 0.15s, filter 0.15s' }}
            />
            <div style={styles.titleGlowInner} />

            {/* Pulse animation on title */}
            <motion.h1
              animate={phase === 'idle' ? {
                scale: [1, 1.008, 1],
                textShadow: [
                  '0 0 28px rgba(168,85,247,0.55), 0 0 60px rgba(168,85,247,0.25)',
                  '0 0 40px rgba(168,85,247,0.85), 0 0 80px rgba(236,72,153,0.35)',
                  '0 0 28px rgba(168,85,247,0.55), 0 0 60px rgba(168,85,247,0.25)',
                ],
              } : {}}
              transition={{ duration: 3.5, repeat: Infinity, ease: 'easeInOut' }}
              style={styles.title}
            >
              STRIKESYNC
            </motion.h1>
          </motion.div>

          {/* Divider line */}
          <motion.div
            initial={{ scaleX: 0, opacity: 0 }}
            animate={{ scaleX: 1, opacity: 1 }}
            transition={{ duration: 0.6, delay: 0.9, ease: EASE_CINEMATIC }}
            style={styles.dividerWrap}
          >
            <div style={styles.divider} />
          </motion.div>

        </div>

        {/* ── Bottom prompt ─────────────────────────────────────────── */}
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          transition={{ duration: 0.5, delay: 1.1 }}
          style={styles.bottomPrompt}
        >
          <motion.span
            animate={{ opacity: phase === 'idle' ? [0.6, 1, 0.6] : 1 }}
            transition={{ duration: 2, repeat: Infinity, ease: 'easeInOut' }}
            style={styles.pressText}
          >
            PRESS ANY BUTTON TO START
          </motion.span>

          {/* 3 indicator dots */}
          <div style={styles.dots}>
            {[0, 1, 2].map(i => (
              <motion.div
                key={i}
                animate={{ opacity: activeDot === i ? 1 : 0.25, scale: activeDot === i ? 1.3 : 1 }}
                transition={{ duration: 0.2 }}
                style={styles.dot}
              />
            ))}
          </div>
        </motion.div>
      </motion.div>

      {/* ── Connection status (bottom-left) ───────────────────────────── */}
      <motion.div
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        transition={{ delay: 1.3 }}
        style={styles.connStatus}
      >
        <span style={styles.connLabel}>CONNECTION STATUS</span>
        <div style={styles.connRow}>
          <span style={{ ...styles.connDot, background: connectionColor,
            boxShadow: `0 0 6px ${connectionColor}` }} />
          <span style={styles.connText}>{connectionLabel}</span>
        </div>
      </motion.div>

      {/* ── Legal text (bottom-right) ──────────────────────────────────── */}
      <motion.div
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        transition={{ delay: 1.3 }}
        style={styles.legal}
      >
        <span style={styles.legalLabel}>LEGAL</span>
        <span style={styles.legalText}>
          © 2024 STRIKE-TECH INDUSTRIES. ALL RIGHTS RESERVED.
        </span>
      </motion.div>

      {/* ── Glow burst overlay ──────────────────────────────────────────── */}
      <AnimatePresence>
        {glowBurst && (
          <motion.div
            key="burst"
            initial={{ opacity: 0.6 }}
            animate={{ opacity: 0 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.4 }}
            style={styles.glowBurst}
          />
        )}
      </AnimatePresence>
    </motion.div>
  );
}

// ─── Background ────────────────────────────────────────────────────────────
function Background({ glowBurst }: { glowBurst: boolean }) {
  return (
    <div style={styles.bg}>
      {/* Deep navy base */}
      <div style={styles.bgBase} />
      {/* Left dark panel gradient */}
      <div style={styles.bgLeft} />
      {/* Center-right purple ambient glow */}
      <motion.div
        animate={{ opacity: glowBurst ? 0.5 : 0.18 }}
        transition={{ duration: 0.2 }}
        style={styles.bgGlow}
      />
      {/* Noise overlay (CSS generated) */}
      <div style={styles.noise} />
    </div>
  );
}

// ─── Corner bracket ────────────────────────────────────────────────────────
function CornerBracket() {
  return (
    <svg
      style={styles.bracket}
      width="80" height="80" viewBox="0 0 80 80"
      fill="none"
    >
      <path d="M4 40 L4 4 L40 4" stroke="rgba(168,85,247,0.35)" strokeWidth="1.5" fill="none" />
    </svg>
  );
}

// ─── Character silhouette (pure CSS / SVG — no image dep) ─────────────────
function CharacterSilhouette() {
  return (
    <motion.div
      animate={{ scaleY: [1, 1.008, 1] }}
      transition={{ duration: 5, repeat: Infinity, ease: 'easeInOut' }}
      style={styles.silhouette}
    >
      <svg
        viewBox="0 0 220 520"
        width="220" height="520"
        fill="none"
        style={{ filter: 'drop-shadow(0 0 18px rgba(100,140,255,0.55))' }}
      >
        {/* Head */}
        <ellipse cx="110" cy="48" rx="28" ry="32" fill="rgba(80,120,220,0.18)" stroke="rgba(100,160,255,0.45)" strokeWidth="1"/>
        {/* Neck */}
        <rect x="100" y="78" width="20" height="18" rx="4" fill="rgba(80,120,220,0.15)" stroke="rgba(100,160,255,0.3)" strokeWidth="1"/>
        {/* Torso */}
        <path d="M65 96 Q60 160 62 220 L158 220 Q160 160 155 96 Q140 88 110 86 Q80 88 65 96Z"
          fill="rgba(60,100,200,0.15)" stroke="rgba(100,160,255,0.4)" strokeWidth="1"/>
        {/* Left shoulder / arm */}
        <path d="M65 100 Q38 112 28 150 Q24 175 30 195 Q40 200 48 188 Q44 165 58 138 Q62 118 68 108Z"
          fill="rgba(60,100,200,0.12)" stroke="rgba(100,160,255,0.35)" strokeWidth="1"/>
        {/* Left forearm / fist (raised — combat pose) */}
        <path d="M30 195 Q24 210 26 235 Q30 255 44 258 Q56 258 58 245 Q50 232 48 215 Q44 202 36 196Z"
          fill="rgba(60,100,200,0.12)" stroke="rgba(100,160,255,0.35)" strokeWidth="1"/>
        {/* Right shoulder / arm */}
        <path d="M155 100 Q178 112 188 148 Q193 172 188 192 Q178 198 170 186 Q174 163 162 138 Q158 118 152 108Z"
          fill="rgba(60,100,200,0.12)" stroke="rgba(100,160,255,0.35)" strokeWidth="1"/>
        {/* Right forearm */}
        <path d="M188 192 Q194 208 192 232 Q188 252 174 254 Q162 254 160 241 Q168 228 170 210 Q174 198 182 192Z"
          fill="rgba(60,100,200,0.12)" stroke="rgba(100,160,255,0.35)" strokeWidth="1"/>
        {/* Hips */}
        <path d="M62 220 Q58 248 60 265 L160 265 Q162 248 158 220Z"
          fill="rgba(60,100,200,0.14)" stroke="rgba(100,160,255,0.3)" strokeWidth="1"/>
        {/* Left leg */}
        <path d="M60 265 Q52 300 50 350 Q48 390 52 420 Q62 428 72 420 Q78 390 78 350 Q80 310 84 270Z"
          fill="rgba(60,100,200,0.12)" stroke="rgba(100,160,255,0.3)" strokeWidth="1"/>
        {/* Left foot */}
        <path d="M52 420 Q46 440 48 460 Q52 468 66 466 Q76 462 76 452 Q72 438 70 422Z"
          fill="rgba(60,100,200,0.10)" stroke="rgba(100,160,255,0.28)" strokeWidth="1"/>
        {/* Right leg */}
        <path d="M160 265 Q166 300 168 350 Q170 390 168 420 Q158 428 148 420 Q142 390 140 350 Q138 310 136 270Z"
          fill="rgba(60,100,200,0.12)" stroke="rgba(100,160,255,0.3)" strokeWidth="1"/>
        {/* Right foot */}
        <path d="M168 420 Q174 440 172 460 Q168 468 154 466 Q144 462 144 452 Q148 438 150 422Z"
          fill="rgba(60,100,200,0.10)" stroke="rgba(100,160,255,0.28)" strokeWidth="1"/>
        {/* Rim lighting highlights */}
        <path d="M28 150 Q22 175 30 195" stroke="rgba(120,180,255,0.7)" strokeWidth="1.5" fill="none"/>
        <path d="M192 148 Q197 172 188 192" stroke="rgba(120,180,255,0.5)" strokeWidth="1.5" fill="none"/>
        <path d="M65 96 Q62 140 62 180" stroke="rgba(140,180,255,0.35)" strokeWidth="1" fill="none"/>
      </svg>
    </motion.div>
  );
}

// ─── Styles ────────────────────────────────────────────────────────────────
const styles: Record<string, React.CSSProperties> = {
  root: {
    position: 'fixed',
    inset: 0,
    cursor: 'pointer',
    userSelect: 'none',
    fontFamily: '"Orbitron", "Rajdhani", "Bebas Neue", monospace',
    overflow: 'hidden',
  },

  // Background layers
  bg: {
    position: 'absolute',
    inset: 0,
    zIndex: 0,
  },
  bgBase: {
    position: 'absolute', inset: 0,
    background: 'linear-gradient(135deg, #020714 0%, #050A1A 40%, #080D22 100%)',
  },
  bgLeft: {
    position: 'absolute', inset: 0,
    background: 'linear-gradient(to right, rgba(2,5,18,0.95) 0%, rgba(2,5,18,0.5) 30%, transparent 55%)',
  },
  bgGlow: {
    position: 'absolute',
    top: '10%', left: '35%',
    width: '55%', height: '80%',
    background: 'radial-gradient(ellipse at 45% 40%, rgba(168,85,247,0.22) 0%, rgba(236,72,153,0.08) 50%, transparent 75%)',
    borderRadius: '50%',
  },
  noise: {
    position: 'absolute', inset: 0,
    backgroundImage: `url("data:image/svg+xml,%3Csvg viewBox='0 0 256 256' xmlns='http://www.w3.org/2000/svg'%3E%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='4' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23n)' opacity='0.035'/%3E%3C/svg%3E")`,
    backgroundRepeat: 'repeat',
    backgroundSize: '256px',
    opacity: 0.6,
    mixBlendMode: 'overlay',
  },

  // Corner bracket
  bracket: {
    position: 'absolute',
    top: 28, left: 28,
    zIndex: 10,
  },

  // Glass panel
  glassPanel: {
    position: 'absolute',
    top: '10%', bottom: '10%',
    left: '26%', right: '2%',
    background: 'rgba(10,18,50,0.35)',
    backdropFilter: 'blur(14px)',
    WebkitBackdropFilter: 'blur(14px)',
    border: '1px solid rgba(168,85,247,0.12)',
    borderRadius: 4,
    zIndex: 5,
    overflow: 'hidden',
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
  },
  scanLines: {
    position: 'absolute', inset: 0,
    backgroundImage: 'repeating-linear-gradient(0deg, transparent, transparent 3px, rgba(0,0,0,0.06) 3px, rgba(0,0,0,0.06) 4px)',
    pointerEvents: 'none',
    zIndex: 1,
  },
  diagonalLines: {
    position: 'absolute', inset: 0,
    backgroundImage: 'repeating-linear-gradient(135deg, transparent, transparent 40px, rgba(168,85,247,0.025) 40px, rgba(168,85,247,0.025) 41px)',
    pointerEvents: 'none',
    zIndex: 1,
  },

  // Silhouette
  silhouetteWrap: {
    position: 'absolute',
    top: '5%', bottom: 0,
    left: 0, right: 0,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    zIndex: 2,
    pointerEvents: 'none',
  },
  silhouette: {
    opacity: 0.28,
  },

  // Center content
  centerStack: {
    position: 'relative',
    zIndex: 10,
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: 0,
    marginTop: '-60px', // shift slightly above center
  },
  subtitle: {
    fontSize: 11,
    fontFamily: '"Rajdhani", "Orbitron", monospace',
    fontWeight: 400,
    letterSpacing: '0.32em',
    color: 'rgba(168,130,220,0.75)',
    marginBottom: 14,
    textAlign: 'center',
  },
  titleWrap: {
    position: 'relative',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
  titleGlow: {
    position: 'absolute',
    width: '110%', height: '180%',
    background: 'radial-gradient(ellipse, rgba(168,85,247,0.5) 0%, rgba(236,72,153,0.25) 50%, transparent 75%)',
    pointerEvents: 'none',
  },
  titleGlowInner: {
    position: 'absolute',
    width: '70%', height: '120%',
    background: 'radial-gradient(ellipse, rgba(220,130,255,0.3) 0%, transparent 65%)',
    pointerEvents: 'none',
  },
  title: {
    position: 'relative',
    margin: 0,
    fontSize: 'clamp(56px, 8vw, 96px)',
    fontFamily: '"Orbitron", "Rajdhani", "Bebas Neue", monospace',
    fontWeight: 900,
    fontStyle: 'italic',
    letterSpacing: '0.04em',
    background: 'linear-gradient(135deg, #C084FC 0%, #A855F7 30%, #EC4899 70%, #F472B6 100%)',
    WebkitBackgroundClip: 'text',
    WebkitTextFillColor: 'transparent',
    backgroundClip: 'text',
    textShadow: '0 0 28px rgba(168,85,247,0.55), 0 0 60px rgba(168,85,247,0.25)',
    whiteSpace: 'nowrap',
  },
  dividerWrap: {
    width: '100%',
    display: 'flex',
    justifyContent: 'center',
    marginTop: 18,
    transformOrigin: 'center',
  },
  divider: {
    width: 120,
    height: 1,
    background: 'linear-gradient(90deg, transparent 0%, rgba(168,85,247,0.8) 40%, rgba(236,72,153,0.8) 60%, transparent 100%)',
    boxShadow: '0 0 8px rgba(168,85,247,0.6)',
  },

  // Bottom prompt
  bottomPrompt: {
    position: 'absolute',
    bottom: '8%',
    left: 0, right: 0,
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: 14,
    zIndex: 10,
  },
  pressText: {
    fontSize: 13,
    fontFamily: '"Rajdhani", "Orbitron", monospace',
    fontWeight: 500,
    letterSpacing: '0.28em',
    color: 'rgba(200,200,220,0.8)',
    textAlign: 'center',
  },
  dots: {
    display: 'flex',
    gap: 10,
    alignItems: 'center',
  },
  dot: {
    width: 6,
    height: 6,
    borderRadius: '50%',
    background: 'rgba(200,200,220,0.9)',
  },

  // Connection status
  connStatus: {
    position: 'absolute',
    bottom: 28, left: 28,
    zIndex: 20,
    display: 'flex',
    flexDirection: 'column',
    gap: 4,
  },
  connLabel: {
    fontSize: 9,
    letterSpacing: '0.22em',
    color: 'rgba(120,120,160,0.7)',
    fontFamily: '"Rajdhani", monospace',
    fontWeight: 500,
  },
  connRow: {
    display: 'flex',
    alignItems: 'center',
    gap: 6,
  },
  connDot: {
    width: 6, height: 6,
    borderRadius: '50%',
    flexShrink: 0,
    transition: 'background 0.3s, box-shadow 0.3s',
  },
  connText: {
    fontSize: 10,
    letterSpacing: '0.12em',
    color: 'rgba(150,150,190,0.75)',
    fontFamily: '"Rajdhani", monospace',
    fontWeight: 400,
  },

  // Legal
  legal: {
    position: 'absolute',
    bottom: 28, right: 28,
    zIndex: 20,
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-end',
    gap: 4,
  },
  legalLabel: {
    fontSize: 9,
    letterSpacing: '0.22em',
    color: 'rgba(120,120,160,0.55)',
    fontFamily: '"Rajdhani", monospace',
    fontWeight: 500,
  },
  legalText: {
    fontSize: 9,
    letterSpacing: '0.08em',
    color: 'rgba(120,120,160,0.45)',
    fontFamily: '"Rajdhani", monospace',
    fontWeight: 300,
  },

  // Glow burst overlay on keypress
  glowBurst: {
    position: 'fixed',
    inset: 0,
    background: 'radial-gradient(ellipse at 60% 45%, rgba(168,85,247,0.35) 0%, transparent 60%)',
    zIndex: 50,
    pointerEvents: 'none',
  },
};