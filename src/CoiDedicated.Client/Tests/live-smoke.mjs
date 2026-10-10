import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createHash, createHmac } from 'node:crypto';
import { mkdtemp, readFile, writeFile, unlink, rmdir } from 'node:fs/promises';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const executable = fileURLToPath(new URL('./bin/Release/net10.0/SnapshotSmoke.dll', import.meta.url));
const secret = 'fixture-only-dedicated-join-secret';
const nonce = Buffer.alloc(32, 7);
const snapshot = Buffer.from('independent persistent game session snapshot fixture');
const u16 = n => { const b = Buffer.alloc(2); b.writeUInt16LE(n); return b; };
const u32 = n => { const b = Buffer.alloc(4); b.writeUInt32LE(n); return b; };
const u64 = n => { const b = Buffer.alloc(8); b.writeBigUInt64LE(BigInt(n)); return b; };
const blob = b => Buffer.concat([u32(b.length), b]);
const text = t => blob(Buffer.from(t));
const frame = (kind, payload) => Buffer.concat([u32(payload.length + 4), u16(1), u16(kind), payload]);
const welcome = stage => frame(2, Buffer.concat([Buffer.from([stage]), ...(stage === 0 ? [blob(nonce)] : []), text('CoI 0.8.7d / fixture')]));
const tick = (sequence, before, after, paused, commands) => frame(7, Buffer.concat([
  u64(sequence), u32(before), u32(after), Buffer.from([Number(paused)]), u16(commands.length), ...commands
]));

async function run(gap, mismatch = false) {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'coi-live-fixture-'));
  const key = path.join(directory, 'join.key');
  const output = path.join(directory, 'snapshot.save');
  await writeFile(key, secret);
  let serverError, completed = false;
  const sockets = new Set();
  const server = net.createServer(socket => {
    sockets.add(socket); socket.on('close', () => sockets.delete(socket));
    socket.on('error', () => {});
    let stage = 0, buffer = Buffer.alloc(0);
    socket.write(welcome(0));
    socket.on('data', bytes => {
      try {
        buffer = Buffer.concat([buffer, bytes]);
        while (buffer.length >= 4 && buffer.length >= buffer.readUInt32LE(0) + 4) {
          const size = buffer.readUInt32LE(0), kind = buffer.readUInt16LE(6);
          assert.equal(buffer.readUInt16LE(4), 1);
          const data = buffer.subarray(8, size + 4); buffer = buffer.subarray(size + 4);
          if (stage === 0) {
            assert.equal(kind, 1);
            const sizeName = data.readUInt32LE(0), name = data.subarray(4, 4 + sizeName).toString();
            const offset = 4 + sizeName;
            assert.equal(data.readUInt32LE(offset), 32);
            assert.deepEqual(data.subarray(offset + 4), createHmac('sha256', secret).update(nonce).update('coi-dedicated-v1\n' + name).digest());
            socket.write(welcome(1));
            socket.write(frame(3, Buffer.concat([u32(123), u64(snapshot.length), blob(createHash('sha256').update(snapshot).digest())])));
            socket.write(frame(4, blob(snapshot)));
            socket.write(frame(5, Buffer.alloc(0)));
          } else if (stage === 1) {
            assert.equal(kind, 8); assert.equal(data.length, 4); assert.equal(data.readInt32LE(), 123);
            socket.write(tick(17, 123, 124, false, [Buffer.concat([u64(0), text('server'), blob(Buffer.from([1, 0, 1, 0, 0]))])]));
          } else if (stage === 2) {
            assert.equal(kind, 6); assert.equal(data.readBigUInt64LE(), 1n);
            assert.equal(data.readUInt32LE(8), 3); assert.deepEqual(data.subarray(12), Buffer.from([1, 2, 3]));
          } else if (stage === 3) {
            assert.equal(kind, 8); assert.equal(data.length, 12);
            assert.equal(data.readBigUInt64LE(), 17n); assert.equal(data.readInt32LE(8), 124);
            completed = true;
            socket.write(frame(11, Buffer.concat([u64(1), text('fixture command rejected')])));
            socket.write(tick(gap ? 19 : 18, 124, 124, true, []));
          } else throw new Error('Unexpected client frame');
          stage++;
        }
      } catch (error) { serverError = error; socket.destroy(); }
    });
  });
  try {
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const result = await new Promise((resolve, reject) => {
      const process = spawn('dotnet', [executable, '--live', 'true', '--port', String(server.address().port), '--secret-file', key, '--output', output, ...(mismatch ? ['--mismatch', 'true'] : [])], { windowsHide: true });
      let stdout = '', stderr = '';
      process.stdout.on('data', data => { stdout += data; });
      process.stderr.on('data', data => { stderr += data; });
      const timer = setTimeout(() => { process.kill(); reject(new Error('Live fixture timeout')); }, 15000);
      process.on('error', error => { clearTimeout(timer); reject(error); });
      process.on('close', code => { clearTimeout(timer); resolve({ code, stdout, stderr }); });
    });
    if (serverError) throw serverError;
    if (mismatch) {
      assert.equal(completed, false); assert.equal(result.code, 1);
      assert.match(result.stderr, /fingerprint or protocol differs/);
      await assert.rejects(readFile(output), { code: 'ENOENT' });
      console.log('PASS live session rejects mismatched assembly identity before transfer');
      return;
    }
    assert(completed); assert.equal(result.code, gap ? 1 : 0, result.stderr);
    if (gap) assert.match(result.stderr, /sequence gap/);
    assert.deepEqual(await readFile(output), snapshot);
    assert(!result.stdout.includes(secret) && !result.stderr.includes(secret));
    console.log(gap ? 'PASS live session rejects tick sequence gap' : 'PASS live join, snapshot, ready, command proposal, tick and ack');
  } finally {
    for (const socket of sockets) socket.destroy();
    await new Promise(resolve => server.close(resolve));
    for (const name of ['join.key', 'snapshot.save', 'snapshot.save.part']) {
      try { await unlink(path.join(directory, name)); } catch (error) { if (error.code !== 'ENOENT') throw error; }
    }
    await rmdir(directory);
  }
}
await run(false);
await run(true);
await run(false, true);
