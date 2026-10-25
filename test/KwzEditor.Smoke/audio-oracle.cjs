// Independent mixed-audio reference; stdin is a KWZ, stdout is a SHA-256 hash.
const {KwzParser}=require(require('path').resolve(process.argv[2]));
const bytes=require('fs').readFileSync(0);
const parser=new KwzParser(bytes.buffer.slice(bytes.byteOffset,bytes.byteOffset+bytes.byteLength));
const pcm=parser.getAudioMasterPcm(16364);
process.stdout.write(require('crypto').createHash('sha256').update(Buffer.from(pcm.buffer,pcm.byteOffset,pcm.byteLength)).digest('hex').toUpperCase());
