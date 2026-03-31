// src/App.tsx — updated with map_select routing

import { AnimatePresence }          from 'framer-motion';
import { useWebSocket }             from './hooks/useWebSocket';
import { useGameStore, GameStore, GameScreen } from './hooks/useGameStore';
import { IntroScreen }              from './components/screens/IntroScreen';
import { CharacterSelectScreen }    from './components/screens/CharacterSelectScreen';
import { MapSelectScreen }          from './components/screens/MapSelectScreen';
import FightIntroScreen from "./components/screens/FightIntroScreen";
function App() {
  useWebSocket(); // single WS init — never call this elsewhere

  const screen         = useGameStore((s: GameStore) => s.screen);
  const wsConnected    = useGameStore((s: GameStore) => s.wsConnected);
  const unityConnected = useGameStore((s: GameStore) => s.unityConnected);

  return (
    <div style={{
      width: '100vw', height: '100vh',
      overflow: 'hidden', background: '#050A1A',
    }}>
      <AnimatePresence mode="wait">
        <ScreenRouter
          key={screen}
          screen={screen}
          wsConnected={wsConnected}
          unityConnected={unityConnected}
        />
      </AnimatePresence>
    </div>
  );
}

interface RouterProps {
  screen:         GameScreen;
  wsConnected:    boolean;
  unityConnected: boolean;
}

function ScreenRouter({ screen, wsConnected, unityConnected }: RouterProps) {
  switch (screen) {
    case 'menu':
      return (
        <IntroScreen
          wsConnected={wsConnected}
          unityConnected={unityConnected}
        />
      );
    case 'char_select':
      return <CharacterSelectScreen />;

    case 'map_select':                    // ← NEW
      return <MapSelectScreen />;

    case 'fight_intro':
      return <FightIntroScreen />;

    case 'countdown':
      return <Placeholder label="COUNTDOWN" />;
    case 'fighting':
      return <Placeholder label="FIGHTING — HUD" />;
    case 'game_over':
      return <Placeholder label="K.O." />;
    default:
      return (
        <IntroScreen
          wsConnected={wsConnected}
          unityConnected={unityConnected}
        />
      );
  }
}

function Placeholder({ label }: { label: string }) {
  return (
    <div style={{
      position: 'fixed', inset: 0, background: '#050A1A',
      display: 'flex', alignItems: 'center', justifyContent: 'center',
    }}>
      <span style={{
        fontFamily: '"Orbitron", monospace', fontSize: 32,
        fontWeight: 700, letterSpacing: '0.2em',
        background: 'linear-gradient(135deg, #A855F7, #EC4899)',
        WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent',
      }}>
        {label}
      </span>
    </div>
  );
}

export default App;