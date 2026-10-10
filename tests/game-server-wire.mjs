// Run against an explicitly started isolated local test server; no game assemblies or npm dependencies.
// This is a transport fixture: echoing checkpoint bytes does NOT validate simulation state.
import assert from 'node:assert/strict';
import net from 'node:net';
import { createHash, createHmac } from 'node:crypto';
import { readFile } from 'node:fs/promises';

const [keyPath, port = '28551'] = process.argv.slice(2);
if (!keyPath) throw new Error('Usage: node tests/game-server-wire.mjs JOIN_KEY_FILE [PORT]');
const secret = (await readFile(keyPath, 'utf8')).replace(/[\r\n]+$/, '');
const u16 = x => { const b = Buffer.alloc(2); b.writeUInt16LE(x); return b; };
const u32 = x => { const b = Buffer.alloc(4); b.writeUInt32LE(x); return b; };
const u64 = x => { const b = Buffer.alloc(8); b.writeBigUInt64LE(BigInt(x)); return b; };
const blob = b => Buffer.concat([u32(b.length), b]);
const text = s => blob(Buffer.from(s));
const frame = (kind, payload = Buffer.alloc(0)) => Buffer.concat([u32(payload.length + 4), u16(1), u16(kind), payload]);
const reader = b => {
  let p = 0;
  return { int: () => { const v = b.readInt32LE(p); p += 4; return v; },
    short: () => { const v = b.readUInt16LE(p); p += 2; return v; },
    bool: () => { const v = b[p++]; assert(v === 0 || v === 1); return !!v; },
    long: () => { const v = b.readBigUInt64LE(p); p += 8; return v; },
    end: () => assert.equal(p, b.length),
    blob: () => { const n = b.readUInt32LE(p); p += 4; assert(n <= b.length - p); const v = b.subarray(p, p + n); p += n; return v; } };
};

async function connect(key = secret) {
  const socket = net.connect({ host: '127.0.0.1', port: Number(port) });
  socket.setTimeout(90000, () => socket.destroy(new Error('Wire fixture timeout'))); // Another joining Unity client may hold ticks for up to 60s.
  let data = Buffer.alloc(0), ended = false, failure;
  const waiters = [];
  const wake = () => waiters.splice(0).forEach(f => f());
  socket.on('data', b => { data = Buffer.concat([data, b]); wake(); });
  socket.on('close', () => { ended = true; wake(); });
  socket.on('error', e => { failure = e; wake(); });
  const read = async () => {
    for (;;) {
      if (data.length >= 4) {
        const n = data.readUInt32LE(); assert(n >= 4 && n <= 1048576);
        if (data.length >= n + 4) {
          assert.equal(data.readUInt16LE(4), 1);
          const result = { kind: data.readUInt16LE(6), payload: data.subarray(8, n + 4) };
          data = data.subarray(n + 4); return result;
        }
      }
      if (failure) throw failure;
      if (ended) return null;
      await new Promise(resolve => waiters.push(resolve));
    }
  };
  const greeting = await read(); assert.equal(greeting.kind, 2); assert.equal(greeting.payload[0], 0);
  const r = reader(greeting.payload.subarray(1)), nonce = r.blob();
  const name = 'wire-' + Math.random().toString(16).slice(2);
  const proof = createHmac('sha256', key).update(nonce).update('coi-dedicated-v1\n' + name).digest();
  socket.write(frame(1, Buffer.concat([text(name), blob(proof)])));
  return { socket, read, send: (k, p) => socket.write(frame(k, p)) };
}

let peer = await connect('incorrect-fixture-key');
try { assert.equal(await peer.read(), null); console.log('PASS incorrect join key rejected before snapshot'); }
finally { peer.socket.destroy(); }

peer = await connect();
try {
  const welcome = await peer.read(); assert.equal(welcome.kind, 2); assert.equal(welcome.payload[0], 1);
  const begin = await peer.read(); assert.equal(begin.kind, 3);
  const r = reader(begin.payload), snapshotStep = r.int(), bytes = r.long(), expected = r.blob();
  let received = 0n; const hash = createHash('sha256');
  for (;;) {
    const next = await peer.read();
    if (next.kind === 5) break;
    assert.equal(next.kind, 4); const chunk = reader(next.payload).blob(); received += BigInt(chunk.length); hash.update(chunk);
  }
  assert.equal(received, bytes); assert.deepEqual(hash.digest(), expected);
  console.log('PASS authenticated snapshot length and SHA-256');
  peer.send(8, u32(snapshotStep));
  // Valid schema, intentionally unsupported speed. No game state should change.
  peer.send(6, Buffer.concat([u64(1), blob(Buffer.concat([u16(1), u16(2), u32(5)]))]));
  let rejected = false, ticks = 0;
  while (!rejected || ticks < 3) {
    const next = await peer.read(); assert(next);
    if (next.kind === 11) {
      const rejectedCommand = reader(next.payload);
      assert.equal(rejectedCommand.long(), 1n);
      assert.match(rejectedCommand.blob().toString(), /speeds 1 through 4/); rejectedCommand.end(); rejected = true;
    } else if (next.kind === 13) {
      // Cosmetic presence snapshots are independent of authoritative commands.
      continue;
    } else {
      assert.equal(next.kind, 7); const t = reader(next.payload), sequence = t.long(), before = t.int(), after = t.int();
      t.bool(); const speed = t.int(); assert(speed >= 1 && speed <= 4); assert(after >= before && after <= before + 4);
      const count = t.short(); assert(count <= 128);
      for (let i = 0; i < count; i++) { t.long(); t.blob(); t.blob(); }
      const checkpoint = t.blob(); assert(checkpoint.length === 0 || checkpoint.length === 32); t.end();
      if (checkpoint.length) peer.send(14, Buffer.concat([u64(sequence), u32(after), blob(checkpoint)]));
      peer.send(8, Buffer.concat([u64(sequence), u32(after)])); ticks++;
    }
  }
  console.log('PASS invalid gameplay proposal rejected while ticks continue');
  // A duplicate request ID is a protocol violation.
  peer.send(6, Buffer.concat([u64(1), blob(Buffer.concat([u16(1), u16(2), u32(1)]))]));
  while (await peer.read()) { /* Drain ticks already in flight. */ }
  console.log('PASS replayed command ID disconnects the peer');
} finally { peer.socket.destroy(); }
console.log('4/4 live server wire checks passed.');
