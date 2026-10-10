// Independent wire fixture; uses Node built-ins only. Build the control client first.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createHash, createHmac, timingSafeEqual } from 'node:crypto';
import { mkdtemp, readFile, writeFile, unlink, rmdir, access } from 'node:fs/promises';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const clientDll = fileURLToPath(new URL('./bin/Release/net10.0/SnapshotSmoke.dll', import.meta.url));
const secret = 'fixture-secret-not-a-real-server-secret';
const nonce = Buffer.from(Array.from({ length: 32 }, (_, i) => i));
const snapshot = Buffer.from('independent simulation snapshot fixture');
const frame = (kind, payload) => {
  const header = Buffer.alloc(8);
  header.writeUInt32LE(payload.length + 4);
  header.writeUInt16LE(1, 4);
  header.writeUInt16LE(kind, 6);
  return Buffer.concat([header, payload]);
};
const u32 = value => { const bytes = Buffer.alloc(4); bytes.writeUInt32LE(value); return bytes; };
const blob = bytes => Buffer.concat([u32(bytes.length), bytes]);
const text = value => blob(Buffer.from(value));
const welcome = stage => frame(2, Buffer.concat([Buffer.from([stage]), ...(stage === 0 ? [blob(nonce)] : []), text('CoI 0.8.7d / fixture')]));

async function runScenario(command, corruptHash = false) {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'coi-control-smoke-'));
  const secretFile = path.join(directory, 'secret.txt');
  const output = path.join(directory, 'snapshot.bin');
  await writeFile(secretFile, secret + '\r\n');
  let serverError;
  let acceptedProof = false;
  let handledCommand = false;
  const sockets = new Set();
  const server = net.createServer(socket => {
    sockets.add(socket);
    socket.on('close', () => sockets.delete(socket));
    socket.on('error', error => { serverError ??= error; });
    let buffer = Buffer.alloc(0);
    let stage = 0;
    socket.write(welcome(0));
    socket.on('data', data => {
      try {
        buffer = Buffer.concat([buffer, data]);
        while (buffer.length >= 4) {
          const length = buffer.readUInt32LE(0);
          assert(length >= 4 && length <= 1048576);
          if (buffer.length < length + 4) break;
          assert.equal(buffer.readUInt16LE(4), 1);
          const kind = buffer.readUInt16LE(6);
          const payload = buffer.subarray(8, 4 + length);
          buffer = buffer.subarray(4 + length);
          let position = 0;
          const takeUInt32 = () => { const result = payload.readUInt32LE(position); position += 4; return result; };
          const takeBlob = () => { const count = takeUInt32(); assert(count <= payload.length - position); const result = payload.subarray(position, position + count); position += count; return result; };
          if (stage === 0) {
            assert.equal(kind, 1);
            const name = takeBlob().toString('utf8');
            const proof = takeBlob();
            assert.equal(position, payload.length);
            assert.equal(name, 'coi-snapshot-preview');
            const expected = createHmac('sha256', Buffer.from(secret)).update(nonce).update(Buffer.from('coi-dedicated-v1\n' + name)).digest();
            assert.equal(proof.length, 32);
            assert(timingSafeEqual(expected, proof), 'HMAC differs from independent Node implementation');
            acceptedProof = true;
            stage = 1;
            socket.write(welcome(1));
          } else {
            assert.equal(stage, 1);
            assert.equal(kind, 6);
            assert.equal(takeUInt32(), 1);
            assert.equal(takeBlob().toString(), command);
            assert.equal(position, payload.length);
            handledCommand = true;
            stage = 2;
            socket.write(frame(8, Buffer.concat([u32(1), Buffer.from([1]), text('{"ok":true}')] )));
            if (command === 'snapshot') {
              const metadata = Buffer.alloc(12);
              metadata.writeInt32LE(123);
              metadata.writeBigInt64LE(BigInt(snapshot.length), 4);
              const digest = corruptHash ? Buffer.alloc(32) : createHash('sha256').update(snapshot).digest();
              socket.write(frame(3, Buffer.concat([metadata, blob(digest)])));
              socket.write(frame(4, blob(snapshot.subarray(0, 7))));
              socket.write(frame(4, blob(snapshot.subarray(7))));
              socket.write(frame(5, Buffer.alloc(0)));
            }
            socket.end();
          }
        }
      } catch (error) {
        serverError = error;
        socket.destroy();
      }
    });
  });
  try {
    await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
    const args = [clientDll, '--port', String(server.address().port), '--secret-file', secretFile, '--command', command];
    if (command === 'snapshot') args.push('--output', output);
    const result = await new Promise((resolve, reject) => {
      const child = spawn('dotnet', args, { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
      let stdout = '', stderr = '';
      child.stdout.on('data', data => { stdout += data; });
      child.stderr.on('data', data => { stderr += data; });
      const timer = setTimeout(() => { child.kill(); reject(new Error('Control fixture timed out.')); }, 15000);
      child.on('error', error => { clearTimeout(timer); reject(error); });
      child.on('close', code => { clearTimeout(timer); resolve({ code, stdout, stderr }); });
    });
    if (serverError) throw serverError;
    assert(acceptedProof && handledCommand, 'Client did not complete handshake and command');
    assert(!result.stdout.includes(secret) && !result.stderr.includes(secret), 'Secret printed in output');
    assert.equal(result.code, corruptHash ? 1 : 0, result.stderr);
    if (command === 'snapshot' && !corruptHash) assert.deepEqual(await readFile(output), snapshot);
    if (corruptHash) {
      assert.match(result.stderr, /SHA-256 did not match/);
      await assert.rejects(access(output));
      await assert.rejects(access(output + '.part'));
    }
    console.log(`PASS ${command}${corruptHash ? ' rejects corrupt snapshot and removes staging file' : ''}`);
  } finally {
    for (const socket of sockets) socket.destroy();
    await new Promise(resolve => server.close(resolve));
    // Remove only named files created in this unique test directory; no recursive deletion.
    for (const name of ['secret.txt', 'snapshot.bin', 'snapshot.bin.part']) {
      try { await unlink(path.join(directory, name)); }
      catch (error) { if (error.code !== 'ENOENT') throw error; }
    }
    await rmdir(directory);
  }
}


await runScenario('snapshot');
await runScenario('snapshot', true);
console.log('2/2 independent snapshot-client smoke tests passed.');
