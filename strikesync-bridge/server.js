/**
 * strikesync-bridge/server.js
 *
 * Production WebSocket relay for StrikeSync.
 *
 * Architecture:
 *   Unity  ──ws://localhost:8080/unity──► this server ──► React clients
 *   React  ──ws://localhost:8080/react──► this server ──► Unity
 *
 * Features added vs prototype:
 *   • Proper path-based client routing (/unity vs /react)
 *   • Ping / pong heartbeat detection (removes stale connections)
 *   • Message validation — malformed JSON is dropped with a warning
 *   • Graceful SIGINT/SIGTERM shutdown
 *   • Reconnect-safe: Unity and React clients can reconnect at any time
 *   • Structured logging with timestamps
 */

const WebSocket = require('ws');
const http      = require('http');

const PORT              = 8080;
const PING_INTERVAL_MS  = 10_000;   // Send ping every 10 s
const PONG_TIMEOUT_MS   = 5_000;    // Consider dead if no pong in 5 s

// ─── State ────────────────────────────────────────────────────────────────────
let unitySocket       = null;
const reactClients    = new Set();

// ─── Server setup ─────────────────────────────────────────────────────────────
const server = http.createServer((req, res) => {
  // Simple health-check endpoint for process monitors / Docker health checks
  res.writeHead(200, { 'Content-Type': 'text/plain' });
  res.end(`StrikeSync Bridge OK — Unity: ${unitySocket ? 'connected' : 'waiting'}, React clients: ${reactClients.size}`);
});

const wss = new WebSocket.Server({ server });

// ─── Helpers ──────────────────────────────────────────────────────────────────
function log(tag, msg) {
  const ts = new Date().toISOString().slice(11, 23); // HH:mm:ss.mmm
  console.log(`[${ts}] [${tag}] ${msg}`);
}

function safeSend(ws, data) {
  if (ws && ws.readyState === WebSocket.OPEN) {
    ws.send(data);
    return true;
  }
  return false;
}

/** Validates that a string is parseable JSON with a 'type' field. */
function validateMessage(raw) {
  try {
    const msg = JSON.parse(raw);
    if (!msg || typeof msg.type !== 'string') {
      log('WARN', `Dropping message with no 'type' field: ${raw.slice(0, 120)}`);
      return null;
    }
    return msg;
  } catch {
    log('WARN', `Dropping non-JSON message: ${raw.slice(0, 120)}`);
    return null;
  }
}

/** Sets up ping/pong heartbeat for a single WebSocket connection. */
function attachHeartbeat(ws, label) {
  ws.isAlive = true;

  ws.on('pong', () => {
    ws.isAlive = true;
  });

  // Also treat an explicit {"type":"pong"} JSON message as a heartbeat reply
  // (NativeWebSocket in Unity handles binary pong frames, but JSON is safer)
  ws._heartbeatTimer = setInterval(() => {
    if (!ws.isAlive) {
      log(label, 'Heartbeat timeout — terminating connection.');
      clearInterval(ws._heartbeatTimer);
      ws.terminate();
      return;
    }
    ws.isAlive = false;
    if (ws.readyState === WebSocket.OPEN) ws.ping();
  }, PING_INTERVAL_MS);

  ws.on('close', () => {
    clearInterval(ws._heartbeatTimer);
  });
}

// ─── Connection handler ───────────────────────────────────────────────────────
wss.on('connection', (ws, req) => {
  const path = req.url || '/';

  if (path === '/unity') {
    handleUnityConnection(ws);
  } else if (path === '/react') {
    handleReactConnection(ws);
  } else {
    log('WARN', `Unknown path '${path}' — closing connection.`);
    ws.close(1008, 'Unknown path');
  }
});

function handleUnityConnection(ws) {
  if (unitySocket && unitySocket.readyState === WebSocket.OPEN) {
    log('UNITY', 'New Unity connection replacing existing one.');
    unitySocket.terminate();
  }

  unitySocket = ws;
  log('UNITY', 'Connected.');
  attachHeartbeat(ws, 'UNITY');

  ws.on('message', (data) => {
    const raw = data.toString();
    const msg = validateMessage(raw);
    if (!msg) return;

    // Handle pong JSON from Unity
    if (msg.type === 'pong') { ws.isAlive = true; return; }

    // Forward to all React clients
    let forwarded = 0;
    reactClients.forEach(client => {
      if (safeSend(client, raw)) forwarded++;
    });

    log('UNITY→REACT', `type="${msg.type}" → ${forwarded} client(s)`);
  });

  ws.on('close', (code) => {
    unitySocket = null;
    log('UNITY', `Disconnected (code ${code}).`);
    // Notify React that Unity is gone so it can show a "connecting" overlay
    const notice = JSON.stringify({ type: 'unity_disconnected' });
    reactClients.forEach(client => safeSend(client, notice));
  });

  ws.on('error', (err) => {
    log('UNITY', `Error: ${err.message}`);
  });

  // Notify existing React clients that Unity just connected
  const notice = JSON.stringify({ type: 'unity_connected' });
  reactClients.forEach(client => safeSend(client, notice));
}

function handleReactConnection(ws) {
  reactClients.add(ws);
  log('REACT', `Client connected. Total: ${reactClients.size}`);
  attachHeartbeat(ws, 'REACT');

  // Immediately tell this client whether Unity is up
  const status = JSON.stringify({
    type: unitySocket ? 'unity_connected' : 'unity_disconnected'
  });
  safeSend(ws, status);

  ws.on('message', (data) => {
    const raw = data.toString();
    const msg = validateMessage(raw);
    if (!msg) return;

    // Handle pong from React
    if (msg.type === 'pong') { ws.isAlive = true; return; }

    // Forward to Unity
    if (safeSend(unitySocket, raw)) {
      log('REACT→UNITY', `type="${msg.type}"`);
    } else {
      log('REACT→UNITY', `DROPPED (Unity not connected) type="${msg.type}"`);
    }
  });

  ws.on('close', (code) => {
    reactClients.delete(ws);
    log('REACT', `Client disconnected (code ${code}). Total: ${reactClients.size}`);
  });

  ws.on('error', (err) => {
    reactClients.delete(ws);
    log('REACT', `Error: ${err.message}`);
  });
}

// ─── Start ────────────────────────────────────────────────────────────────────
server.listen(PORT, () => {
  log('BRIDGE', `Running on ws://localhost:${PORT}`);
  log('BRIDGE', `  Unity endpoint : ws://localhost:${PORT}/unity`);
  log('BRIDGE', `  React endpoint : ws://localhost:${PORT}/react`);
  log('BRIDGE', `  Health check   : http://localhost:${PORT}/`);
});

// ─── Graceful shutdown ────────────────────────────────────────────────────────
function shutdown(signal) {
  log('BRIDGE', `${signal} received — shutting down.`);
  wss.clients.forEach(ws => ws.terminate());
  server.close(() => {
    log('BRIDGE', 'Server closed. Goodbye.');
    process.exit(0);
  });
}

process.on('SIGINT',  () => shutdown('SIGINT'));
process.on('SIGTERM', () => shutdown('SIGTERM'));
