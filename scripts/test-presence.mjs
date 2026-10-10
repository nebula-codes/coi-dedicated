import assert from 'node:assert/strict';
import net from 'node:net';
import { createHmac } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { setTimeout as delay } from 'node:timers/promises';

// Run only against an isolated server: downloads/acknowledges ticks without simulating the island.
// Checkpoints are echoed to exercise transport only; this is not a determinism test.
const [port, keyPath] = process.argv.slice(2);
if (!port || !keyPath) throw Error('Usage: node scripts/test-presence.mjs ISOLATED_PORT JOIN_KEY_FILE');
const secret = (await readFile(keyPath, 'utf8')).trimEnd();
const u16 = n => { const b=Buffer.alloc(2); b.writeUInt16LE(n); return b; };
const u32 = n => { const b=Buffer.alloc(4); b.writeUInt32LE(n); return b; };
const f32 = n => { const b=Buffer.alloc(4); b.writeFloatLE(n); return b; };
const blob = b => Buffer.concat([u32(b.length),b]);
const text = s => blob(Buffer.from(s));
const point = (x,y,z) => Buffer.concat([f32(x),f32(y),f32(z)]);
const pose = Buffer.concat([u16(1),Buffer.from([1]),point(123,4,456),Buffer.from([0,0]),u16(1),text('test-building'),Buffer.from([1]),
  point(1,2,3),point(5,2,3),point(5,2,7),point(1,2,7)]);

function entries(buffer) {
  let offset=0;
  const short=()=>{let n=buffer.readUInt16LE(offset);offset+=2;return n;};
  const int=()=>{let n=buffer.readUInt32LE(offset);offset+=4;return n;};
  const str=()=>{let n=int(),s=buffer.subarray(offset,offset+n).toString();offset+=n;return s;};
  const p=()=>{let r=[buffer.readFloatLE(offset),buffer.readFloatLE(offset+4),buffer.readFloatLE(offset+8)];offset+=12;return r;};
  assert.equal(short(),1);const count=short(),result=[];
  for(let i=0;i<count;i++) {
    const entry={id:int(),name:str(),visible:buffer[offset++]};
    if(entry.visible) {
      entry.center=p(); const quad=buffer[offset++]; if(quad)for(let j=0;j<4;j++)p();
      entry.truncated=buffer[offset++];const previews=short();entry.previews=[];
      for(let j=0;j<previews;j++) {const prototype=str();assert.equal(buffer[offset++],1);entry.previews.push({prototype,corners:[p(),p(),p(),p()]});}
    }
    result.push(entry);
  }
  assert.equal(offset,buffer.length);return result;
}
async function connect(name) {
  const client={name,latest:[],ticks:0,failure:null,ready:false};
  const socket=net.connect({host:'127.0.0.1',port:Number(port)});client.close=()=>socket.destroy();
  const send=(kind,payload)=>socket.write(Buffer.concat([u32(payload.length+4),u16(1),u16(kind),payload]));
  client.sendPresence=payload=>send(12,payload);
  let buffer=Buffer.alloc(0),step;
  socket.on('error',e=>client.failure=e);
  socket.on('data',data=>{
    try {
      buffer=Buffer.concat([buffer,data]);
      while(buffer.length>=4 && buffer.length>=buffer.readUInt32LE(0)+4) {
        let size=buffer.readUInt32LE(0),kind=buffer.readUInt16LE(6),b=buffer.subarray(8,size+4);buffer=buffer.subarray(size+4);
        if(kind===2 && b[0]===0) {
          assert.equal(b.readUInt32LE(1),32);
          const proof=createHmac('sha256',secret).update(b.subarray(5,37)).update('coi-dedicated-v1\n'+name).digest();
          send(1,Buffer.concat([text(name),blob(proof)]));
        } else if(kind===3) step=b.readInt32LE(0);
        else if(kind===5) {send(8,u32(step));client.ready=true;}
        else if(kind===7) {
          client.ticks++;
          assert(b.length>=27,'Truncated revision5 tick');
          assert(b[16]<=1);assert(b.readInt32LE(17)>=1 && b.readInt32LE(17)<=4);
          let offset=23;const count=b.readUInt16LE(21);
          const skipBlob=()=>{assert(offset+4<=b.length);const n=b.readUInt32LE(offset);offset+=4;assert(offset+n<=b.length);offset+=n;};
          for(let i=0;i<count;i++){offset+=8;skipBlob();skipBlob();}
          assert(offset+4<=b.length);const length=b.readUInt32LE(offset);offset+=4;
          assert(length===0 || length===32);assert.equal(offset+length,b.length);
          if(length)send(14,Buffer.concat([b.subarray(0,8),b.subarray(12,16),blob(b.subarray(offset))]));
          send(8,Buffer.concat([b.subarray(0,8),b.subarray(12,16)]));
        }
        else if(kind===13) client.latest=entries(b);
        else if(kind===9 || kind===15) throw Error('Isolated server returned an error or requested resynchronization');
      }
    } catch(e){client.failure=e;socket.destroy();}
  });
  for(let i=0;i<500 && !client.ready && !client.failure;i++)await delay(20);
  if(client.failure)throw client.failure;
  assert(client.ready,'Snapshot join timed out');return client;
}
const sender=await connect('presence-test-sender'),observer=await connect('presence-test-observer');
try {
  sender.sendPresence(pose);await delay(600);
  if(sender.failure||observer.failure)throw sender.failure||observer.failure;
  const entry=observer.latest.find(e=>e.name===sender.name);
  assert(entry && entry.id>0);assert.deepEqual(entry.center,[123,4,456]);
  assert.equal(entry.previews[0].prototype,'test-building');assert.deepEqual(entry.previews[0].corners[2],[5,2,7]);
  console.log('PASS authenticated camera and building preview relay between two peers');
  sender.sendPresence(Buffer.concat([u16(1),Buffer.from([0])]));await delay(400);
  assert(!observer.latest.some(e=>e.name===sender.name));console.log('PASS hidden presence is removed');
  sender.sendPresence(pose);await delay(400);assert(observer.latest.some(e=>e.name===sender.name));
  const ticks=observer.ticks;await delay(3300);
  assert(!observer.latest.some(e=>e.name===sender.name));assert(observer.ticks>ticks);
  console.log('PASS stale presence expires while authoritative ticks continue');
  sender.sendPresence(pose);await delay(400);assert(observer.latest.some(e=>e.name===sender.name));
  sender.close();await delay(400);assert(!observer.latest.some(e=>e.name===sender.name));
  console.log('PASS disconnected presence is removed');
} finally {sender.close();observer.close();}
