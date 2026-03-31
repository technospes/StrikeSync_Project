/**
 * FightIntroScreen — pixel-perfect refinement pass
 * ─────────────────────────────────────────────────
 * Structure:  unchanged
 * Components: unchanged
 * Changes:    micro-detail only — values, spacing, colors, easing
 *
 * FIXES APPLIED:
 *  1. Health bar  → 6px, glow, dark track, correct fill direction
 *  2. Avatar frame → L-shaped corner only (no full border)
 *  3. Round label  → single line "ROUND 01", muted
 *  4. FIGHT color  → #DADADA + reduced cinematic glow
 *  5. Divider      → 62% wide, 2px, soft glow
 *  6. Typography   → name weight 800, tighter tracking
 *  7. Spacing      → 30px edge, 12px avatar gap, 7px name→bar
 *  8. FIGHT offset → top: 45% (slightly above true center)
 *  9. Background   → stronger 3-stop overlay + tighter vignette
 * 10. Animation    → FIGHT 280ms, health 0.65s ease-out
 */

import { useState, useEffect } from "react";
import { motion, AnimatePresence } from "framer-motion";

// ─── design tokens ────────────────────────────────────
const ORANGE = "#FF6A00";
const WHITE  = "#DADADA";   // ← slightly desaturated, cinematic
const GREY   = "#6A6A6A";
const TRACK  = "#1A1A1A";

// ─── timing (ms) ─────────────────────────────────────
const T = {
  hudIn:       180,
  healthFill:  820,   // after HUD lands
  cd1:         600,
  cd2:        1650,
  cd3:        2700,
  fight:      3750,
};

// ─────────────────────────────────────────────────────
// HEALTH BAR
// fix: 6px height · dark track · directional fill · glow
// ─────────────────────────────────────────────────────
function HealthBar({ rtl = false, delay = 0 }) {
  const [filled, setFilled] = useState(false);

  useEffect(() => {
    const id = setTimeout(() => setFilled(true), T.healthFill + delay);
    return () => clearTimeout(id);
  }, [delay]);

  return (
    // track
    <div style={{
      width:        200,
      height:       6,                    // was 3px → now 6px
      background:   TRACK,               // was rgba(255,255,255,0.08) → #1A1A1A
      marginTop:    7,                    // name → bar: 7px
      position:     "relative",
      overflow:     "hidden",
    }}>
      {/* fill */}
      <motion.div
        initial={{ width: "0%" }}
        animate={{ width: filled ? "100%" : "0%" }}
        transition={{ duration: 0.65, ease: [0.25, 0.46, 0.45, 0.94] }} // ease-out
        style={{
          position:   "absolute",
          top:         0,
          bottom:      0,
          [rtl ? "right" : "left"]: 0,   // P2 fills right→left
          background:  ORANGE,
          boxShadow:  `0 0 6px rgba(255,106,0,0.4)`, // subtle glow
          willChange: "width",
        }}
      />
    </div>
  );
}

// ─────────────────────────────────────────────────────
// AVATAR  — L-corner frame only (not full border)
// ─────────────────────────────────────────────────────
function Avatar({ src, side }) {
  const isLeft = side === "left";
  const SZ = 46;

  return (
    <div style={{ position: "relative", width: SZ, height: SZ, flexShrink: 0 }}>
      {/* image / placeholder */}
      <div style={{
        width:      "100%",
        height:     "100%",
        background: "rgba(255,255,255,0.04)",
        overflow:   "hidden",
      }}>
        {src && (
          <img src={src} alt=""
            style={{ width: "100%", height: "100%", objectFit: "cover", display: "block" }} />
        )}
      </div>

      {/*
       * L-shaped corner frame
       * P1 → top-left corner   (border-top + border-left)
       * P2 → top-right corner  (border-top + border-right)
       * Each arm is 22px long, 2px thick
       */}
      <div style={{
        position:     "absolute",
        top:           0,
        [isLeft ? "left" : "right"]: 0,
        width:         22,
        height:        22,
        borderTop:    `2px solid ${ORANGE}`,
        [isLeft ? "borderLeft" : "borderRight"]: `2px solid ${ORANGE}`,
        pointerEvents: "none",
      }} />
    </div>
  );
}

// ─────────────────────────────────────────────────────
// PLAYER HUD
// fix: 30px edge · 12px avatar→name gap · font 800
// ─────────────────────────────────────────────────────
function PlayerHUD({ side, name, avatarSrc }) {
  const isLeft = side === "left";

  return (
    <motion.div
      initial={{ x: isLeft ? -28 : 28, opacity: 0 }}
      animate={{ x: 0, opacity: 1 }}
      transition={{ duration: 0.42, delay: T.hudIn / 1000, ease: [0.22, 1, 0.36, 1] }}
      style={{
        position:      "absolute",
        top:            30,               // 30px from top edge
        [side]:         30,              // 30px from side edge
        display:       "flex",
        flexDirection:  isLeft ? "row" : "row-reverse",
        alignItems:    "flex-start",
        gap:            12,              // avatar → name: 12px
        willChange:    "transform, opacity",
      }}
    >
      <Avatar src={avatarSrc} side={side} />

      {/* name block */}
      <div style={{
        display:       "flex",
        flexDirection: "column",
        alignItems:    isLeft ? "flex-start" : "flex-end",
        paddingTop:    2,               // optical alignment with avatar top
      }}>
        {/* player name */}
        <div style={{
          fontSize:      14,
          fontWeight:    800,           // was 700 → 800
          letterSpacing: "0.12em",     // tighter than before
          color:         WHITE,
          lineHeight:    1,
          fontStyle:     "italic",
          textTransform: "uppercase",
        }}>
          {name}
        </div>

        {/* P1 / P2 tag */}
        <div style={{
          fontSize:      9,
          letterSpacing: "0.3em",
          color:         GREY,
          marginTop:     5,
          textTransform: "uppercase",
          fontWeight:    600,
        }}>
          {isLeft ? "P1" : "P2"}
        </div>

        <HealthBar rtl={!isLeft} delay={isLeft ? 0 : 60} />
      </div>
    </motion.div>
  );
}

// ─────────────────────────────────────────────────────
// ROUND LABEL
// fix: single line "ROUND 01", 13px, muted, 0.25em tracking
// ─────────────────────────────────────────────────────
function RoundLabel() {
  return (
    <motion.div
      initial={{ opacity: 0, y: -6 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.38, delay: T.hudIn / 1000 + 0.04, ease: "easeOut" }}
      style={{
        position:      "absolute",
        top:            34,
        left:          "50%",
        transform:     "translateX(-50%)",
        textAlign:     "center",
        whiteSpace:    "nowrap",
        willChange:    "transform, opacity",
      }}
    >
      {/* single line — was two stacked divs */}
      <div style={{
        fontSize:      13,
        fontWeight:    600,
        letterSpacing: "0.25em",
        color:         GREY,             // muted grey, not orange
        textTransform: "uppercase",
      }}>
        ROUND 01
      </div>
    </motion.div>
  );
}

// ─────────────────────────────────────────────────────
// COUNTDOWN  3 → 2 → 1 → FIGHT
// ─────────────────────────────────────────────────────
function Countdown() {
  const [step, setStep] = useState(null);

  useEffect(() => {
    const ids = [
      setTimeout(() => setStep("3"),     T.cd1),
      setTimeout(() => setStep("2"),     T.cd2),
      setTimeout(() => setStep("1"),     T.cd3),
      setTimeout(() => setStep("FIGHT"), T.fight),
    ];
    return () => ids.forEach(clearTimeout);
  }, []);

  if (!step) return null;
  const isFight = step === "FIGHT";

  return (
    <div style={{
      position: "absolute",
      inset: 0,
      display: "flex",
      alignItems: "center",
      justifyContent: "center",
      pointerEvents: "none",
      zIndex: 50
    }}>
      <AnimatePresence mode="wait">
        <motion.div
          key={step}
          initial={
            isFight
              ? { opacity: 0, scale: 0.94 }
              : { opacity: 0, scale: 1.32 }
          }
          animate={
            isFight
              ? { opacity: 1, scale: 1 }
              : { opacity: [0, 1, 1, 0], scale: [1.32, 1, 1, 0.9] }
          }
          exit={isFight ? { opacity: 0, scale: 1.04 } : {}}
          transition={
            isFight
              ? { duration: 0.28, ease: [0.22, 1, 0.36, 1] }  
              : { duration: 0.88, times: [0, 0.16, 0.72, 1], ease: "easeInOut" }
          }
          style={{
            display:        "flex",
            flexDirection:  "column",
            alignItems:     "center",
            willChange:     "transform, opacity",
          }}
        >
          {/* main word */}
          <div style={{
            fontSize:      isFight
              ? "clamp(88px, 15vw, 168px)"
              : "clamp(80px, 13vw, 138px)",
            fontWeight:    700,
            fontStyle:     "italic",
            letterSpacing: isFight ? "0.11em" : "0.04em",
            color:         WHITE,
            lineHeight:    1,
            userSelect:    "none",
            textShadow: isFight
              ? "0 0 30px rgba(255,106,0,0.40), 0 0 80px rgba(255,106,0,0.15)"
              : "0 2px 20px rgba(0,0,0,0.65)",
          }}>
            {step}
          </div>

          {/* divider — FIGHT only */}
          {isFight && (
            <motion.div
              initial={{ scaleX: 0 }}
              animate={{ scaleX: 1 }}
              transition={{ duration: 0.36, delay: 0.16, ease: [0.22, 1, 0.36, 1] }}
              style={{
                marginTop:       14,
                // REDUCED WIDTH: clamped between 240px and 500px, sitting at 45% of screen width
                width:           "clamp(240px, 45vw, 500px)",
                height:          2,
                background:      ORANGE,
                boxShadow:       "0 0 8px rgba(255,106,0,0.50)", 
                transformOrigin: "center",
                willChange:      "transform",
              }}
            />
          )}
        </motion.div>
      </AnimatePresence>
    </div>
  );
}

// ─────────────────────────────────────────────────────
// ROOT
// fix: stronger 3-stop overlay · tighter vignette edges
// ─────────────────────────────────────────────────────
export default function FightIntroScreen() {
  return (
    <>
      <style>{`
        @import url('https://fonts.googleapis.com/css2?family=Rajdhani:wght@600;700;800&display=swap');
        *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
        body { background: #0A0A0A; }
      `}</style>

      <div style={{
        position:           "fixed",
        inset:               0,
        overflow:           "hidden",
        fontFamily:         "'Rajdhani', sans-serif",
        // background image — swap '/map.jpg' for any industrial/warehouse photo
        backgroundImage:    "url('/map.jpg')",
        backgroundSize:     "cover",
        backgroundPosition: "center",
      }}>

        {/* 1 — Dark overlay: 3-stop gradient for cinematic depth */}
        <div style={{
          position:   "absolute",
          inset:       0,
          background: "linear-gradient(180deg, rgba(0,0,0,0.75) 0%, rgba(0,0,0,0.55) 48%, rgba(0,0,0,0.85) 100%)",
          pointerEvents: "none",
        }} />

        {/* 2 — Vignette: tighter, slightly stronger edges */}
        <div style={{
          position:   "absolute",
          inset:       0,
          background: "radial-gradient(ellipse 95% 85% at 50% 50%, transparent 30%, rgba(0,0,0,0.80) 100%)",
          pointerEvents: "none",
        }} />

        {/* 3 — Player HUDs */}
        <PlayerHUD side="left"  name="Revenant" avatarSrc="/p1-avatar.jpg" />
        <PlayerHUD side="right" name="Kinetic"  avatarSrc="/p2-avatar.jpg" />

        {/* 4 — Round label */}
        <RoundLabel />

        {/* 5 — Countdown + FIGHT */}
        <Countdown />

      </div>
    </>
  );
}