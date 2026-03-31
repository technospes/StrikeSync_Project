import React, { useState, useEffect } from 'react';

const FightHUD = () => {
  // Optional: Simple state to test health bar animations later
  const [p1Health, setP1Health] = useState(100);
  const [p2Health, setP2Health] = useState(100);

  return (
    // MAIN WRAPPER: Full screen, transparent, ignores clicks so Unity can breathe
    <div className="absolute inset-0 w-screen h-screen overflow-hidden text-white font-sans select-none pointer-events-none flex flex-col justify-between">
      
      {/* ==========================================
          TOP NAVIGATION HEADER (Z-20)
      ========================================== */}
      <header className="w-full h-16 bg-black/40 border-b border-[#333333] flex items-center justify-between px-8 z-20 pointer-events-auto backdrop-blur-sm">
        <h1 className="text-3xl font-black italic text-[#FF6B00] tracking-tighter">STRIKESYNC</h1>
        
        <nav className="flex space-x-10 text-[13px] font-bold italic tracking-widest">
          <div className="text-[#FF6B00] border-b-[3px] border-[#FF6B00] pb-1 cursor-pointer">FIGHT</div>
          <div className="text-[#B3B3B3] hover:text-white transition-colors pb-1 cursor-pointer">CUSTOMIZE</div>
          <div className="text-[#B3B3B3] hover:text-white transition-colors pb-1 cursor-pointer">STORE</div>
          <div className="text-[#B3B3B3] hover:text-white transition-colors pb-1 cursor-pointer">SETTINGS</div>
        </nav>

        <div className="flex space-x-6 items-center">
          {/* Bell Icon */}
          <svg className="w-5 h-5 text-white cursor-pointer hover:text-[#FF6B00] transition-colors" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9" />
          </svg>
          {/* Profile Avatar Placeholder */}
          <div className="w-8 h-8 rounded-full bg-white flex items-center justify-center cursor-pointer">
            <svg className="w-5 h-5 text-black" fill="currentColor" viewBox="0 0 20 20">
              <path fillRule="evenodd" d="M10 9a3 3 0 100-6 3 3 0 000 6zm-7 9a7 7 0 1114 0H3z" clipRule="evenodd" />
            </svg>
          </div>
        </div>
      </header>

      {/* ==========================================
          MAIN FIGHT HUD (HEALTH BARS & TIMER) (Z-30)
      ========================================== */}
      <div className="absolute top-24 left-0 w-full px-12 flex justify-between z-30">
        
        {/* --- PLAYER 1 (DRAKE.V) --- */}
        <div className="w-[38%] relative">
          {/* Debug Info */}
          <div className="absolute -top-6 left-0 bg-[#1A1A1A] text-[#B3B3B3] text-[9px] px-2 py-1 font-mono tracking-wider border border-[#333333]">
            <div>FRAME: +02</div>
            <div className="text-white">POS: STANDING</div>
          </div>
          
          <div className="flex flex-col mt-2">
            <h2 className="text-[42px] font-black italic tracking-wide leading-none drop-shadow-md">DRAKE.V</h2>
            
            {/* Round Counters */}
            <div className="flex space-x-1 mt-2 mb-3">
              <div className="w-5 h-3 bg-[#FF6B00]"></div>
              <div className="w-5 h-3 bg-[#FF6B00]"></div>
              <div className="w-5 h-3 bg-[#333333]"></div>
            </div>
            
            {/* Health Bar (Slanted Right Edge) */}
            <div className="w-full h-8 bg-black/60 backdrop-blur-sm border border-[#333333] border-r-0 shadow-lg" 
                 style={{ clipPath: 'polygon(0 0, 100% 0, 98% 100%, 0 100%)' }}>
              <div 
                className="h-full bg-[#FF6B00] transition-all duration-300 ease-out"
                style={{ width: `${p1Health}%` }}
              ></div>
            </div>
          </div>
        </div>

        {/* --- CENTER TIMER --- */}
        <div className="flex flex-col items-center justify-start w-[15%] pt-2">
          <div className="text-[72px] font-black italic leading-none drop-shadow-xl">99</div>
          <div className="w-24 h-[3px] bg-white my-2 shadow-[0_0_10px_rgba(255,255,255,0.5)]"></div>
          <div className="flex items-center space-x-2 text-[11px] font-bold tracking-[0.2em]">
            <div className="w-2 h-2 bg-[#FF6B00]"></div>
            <span>ROUND 1</span>
            <div className="w-2 h-2 bg-[#333333]"></div>
          </div>
        </div>

        {/* --- PLAYER 2 (SABRE) --- */}
        <div className="w-[38%] flex flex-col items-end relative mt-2">
          <div className="flex items-end space-x-4">
            <h2 className="text-[42px] font-black italic tracking-wide leading-none drop-shadow-md">SABRE</h2>
            {/* P2 Avatar Square */}
            <div className="w-12 h-12 bg-gray-800 border border-[#333333] overflow-hidden flex items-center justify-center">
                <svg className="w-8 h-8 text-gray-600" fill="currentColor" viewBox="0 0 20 20"><path fillRule="evenodd" d="M10 9a3 3 0 100-6 3 3 0 000 6zm-7 9a7 7 0 1114 0H3z" clipRule="evenodd" /></svg>
            </div>
          </div>
          
          {/* Round Counters */}
          <div className="flex space-x-1 mt-2 mb-3">
            <div className="w-5 h-3 bg-[#333333]"></div>
            <div className="w-5 h-3 bg-[#333333]"></div>
            <div className="w-5 h-3 bg-[#333333]"></div>
          </div>
          
          {/* Health Bar (Slanted Left Edge) */}
          <div className="w-full h-8 bg-black/60 backdrop-blur-sm border border-[#333333] border-l-0 shadow-lg" 
               style={{ clipPath: 'polygon(2% 0, 100% 0, 100% 100%, 0 100%)' }}>
            <div 
              className="h-full bg-[#FF6B00] ml-auto transition-all duration-300 ease-out"
              style={{ width: `${p2Health}%` }}
            ></div>
          </div>
        </div>
      </div>

      {/* ==========================================
          CENTER EVENT OVERLAY (COMBO) (Z-50)
      ========================================== */}
      <div className="absolute top-1/2 left-1/2 transform -translate-x-1/2 -translate-y-1/2 flex flex-col items-center z-50">
        <div className="relative flex items-center justify-center h-32 w-64">
          <span className="text-[160px] font-black italic text-white/10 absolute -ml-12 drop-shadow-2xl">14</span>
          <span className="text-[64px] font-black italic text-[#FF6B00] z-10 mt-12 ml-16 drop-shadow-[0_0_15px_rgba(255,107,0,0.5)]">HITS</span>
        </div>
        <div className="bg-[#FF6B00] text-black font-black italic px-6 py-1 mt-2 text-[22px] tracking-wide">
          CRITICAL STRIKE
        </div>
      </div>

      {/* ==========================================
          BOTTOM METERS & FOOTER
      ========================================== */}
      <div className="w-full flex flex-col mt-auto z-30">
        
        {/* SUPER METERS */}
        <div className="w-full px-12 flex justify-between mb-6">
          {/* P1 Meter */}
          <div className="w-[42%]">
            <div className="flex justify-between items-end mb-1 font-bold italic tracking-widest">
              <span className="text-[12px] text-[#B3B3B3]">KINETIC DRIVE</span>
              <span className="text-2xl text-white leading-none">MAX</span>
            </div>
            <div className="flex space-x-1 h-5">
              <div className="flex-1 bg-white shadow-[0_0_10px_rgba(255,255,255,0.4)]"></div>
              <div className="flex-1 bg-white shadow-[0_0_10px_rgba(255,255,255,0.4)]"></div>
              <div className="flex-1 bg-white shadow-[0_0_10px_rgba(255,255,255,0.4)]"></div>
            </div>
          </div>

          {/* P2 Meter */}
          <div className="w-[42%] flex flex-col items-end">
            <div className="flex justify-between items-end mb-1 w-full font-bold italic tracking-widest">
              <span className="text-2xl text-white leading-none">LEVEL 1</span>
              <span className="text-[12px] text-[#B3B3B3]">SYNCHRO BURST</span>
            </div>
            <div className="flex space-x-1 h-5 w-full">
              <div className="flex-1 bg-[#D9D9D9]"></div>
              <div className="flex-1 bg-[#242424] border border-[#333333]"></div>
              <div className="flex-1 bg-[#242424] border border-[#333333]"></div>
            </div>
          </div>
        </div>

        {/* INPUT LEGEND FOOTER (Z-20) */}
        <footer className="w-full h-12 bg-black/60 border-t border-[#333333] flex items-center justify-end px-12 space-x-8 text-[11px] font-bold tracking-[0.2em] backdrop-blur-sm pointer-events-auto">
          <div className="flex items-center space-x-2 text-[#FF6B00] cursor-pointer hover:brightness-125 transition">
            <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 20 20"><path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clipRule="evenodd" /></svg>
            <span>SELECT</span>
          </div>
          <div className="flex items-center space-x-2 text-[#B3B3B3] cursor-pointer hover:text-white transition">
            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M10 14l2-2m0 0l2-2m-2 2l-2-2m2 2l2 2m7-2a9 9 0 11-18 0 9 9 0 0118 0z" /></svg>
            <span>BACK</span>
          </div>
          <div className="flex items-center space-x-2 text-[#B3B3B3] cursor-pointer hover:text-white transition">
            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" /><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" /></svg>
            <span>VIEW</span>
          </div>
          <div className="flex items-center space-x-2 text-[#B3B3B3] cursor-pointer hover:text-white transition">
            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M4 6h16M4 12h16M4 18h16" /></svg>
            <span>MENU</span>
          </div>
        </footer>

      </div>
    </div>
  );
};

export default FightHUD;