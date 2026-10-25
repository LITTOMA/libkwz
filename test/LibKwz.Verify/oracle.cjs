// Independent oracle: pass the unmodified flipnote.js 6.3.0 KwzParser.cjs as argv[2].
// Input is one KWZ on stdin; output contains hashes only, never decoded media files.
const { KwzParser } = require(require('path').resolve(process.argv[2]));
const { createHash } = require('crypto');
const data = require('fs').readFileSync(0);
const parser = new KwzParser(data.buffer.slice(data.byteOffset, data.byteOffset + data.byteLength));
const frames = [];
for (let i = 0; i < parser.frameCount; i++) {
  const hash = createHash('sha256');
  for (const layer of parser.decodeFrame(i)) hash.update(layer);
  frames.push(hash.digest('hex').toUpperCase());
}
const audio = [];
if (!parser.isComment && !parser.isFolderIcon) {
  for (const id of parser.audioTracks) {
    const pcm = parser.decodeAudioTrack(id);
    audio.push(createHash('sha256').update(Buffer.from(pcm.buffer, pcm.byteOffset, pcm.byteLength)).digest('hex').toUpperCase());
  }
}
let signatureValid = null;
if (process.argv[3]) {
  const publicDer = require('fs').readFileSync(process.argv[3]);
  const key = require('crypto').createPublicKey({key:publicDer,format:'der',type:'pkcs1'});
  signatureValid = require('crypto').verify('sha256',data.subarray(0,-256),key,data.subarray(-256));
}
process.stdout.write(JSON.stringify({ frames, audio, signatureValid }));
