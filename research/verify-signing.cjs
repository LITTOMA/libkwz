// Validate the KWZ signing key embedded in the user's own game binary.
// Does not export key material or write modified game/animation files.
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const [binaryPath, corpusRoot, reportPath] = process.argv.slice(2);
if (!binaryPath || !corpusRoot) throw Error('Usage: node verify-signing.cjs <code-decompressed.bin> <unpack directory> [report.json]');
const binary = fs.readFileSync(binaryPath);
const offset = 0x283800;
if (!binary.subarray(offset, offset + 4).equals(Buffer.from([0x30,0x82,0x04,0xA4]))) throw Error('Unexpected key location; this script targets the analyzed Ugomemo binary.');
const keyBytes = binary.subarray(offset, offset + 1192);
const privateKey = crypto.createPrivateKey({key:keyBytes, format:'der', type:'pkcs1'});
const publicKey = crypto.createPublicKey({key:binary.subarray(0x2836f0,0x2836f0+270),format:'der',type:'pkcs1'});
if (!crypto.createPublicKey(privateKey).export({format:'der',type:'pkcs1'}).equals(publicKey.export({format:'der',type:'pkcs1'}))) throw Error('Embedded public and private keys do not match.');
const walk = function*(directory) {
  for (const item of fs.readdirSync(directory, {withFileTypes:true})) {
    const filename=path.join(directory,item.name);
    if(item.isDirectory()) yield* walk(filename);
    else if(filename.toLowerCase().endsWith('.kwz')) yield filename;
  }
};
const rows=[];
for(const filename of walk(corpusRoot)) {
  const file=fs.readFileSync(filename), body=file.subarray(0,-256), signature=file.subarray(-256);
  const generated=crypto.sign('sha256',body,{key:privateKey,padding:crypto.constants.RSA_PKCS1_PADDING});
  const changed=Buffer.from(body); changed[12]^=1;
  const newSignature=crypto.sign('sha256',changed,{key:privateKey,padding:crypto.constants.RSA_PKCS1_PADDING});
  const originalValid=crypto.verify('sha256',body,publicKey,signature);
  const reproduced=generated.equals(signature);
  const oldSignatureRejected=!crypto.verify('sha256',changed,publicKey,signature);
  const newSignatureValid=crypto.verify('sha256',changed,publicKey,newSignature);
  if(!originalValid || !reproduced || !oldSignatureRejected || !newSignatureValid) throw Error(`Signing check failed: ${filename}`);
  rows.push({path:path.relative(corpusRoot,filename),bytes:file.length,sha256:crypto.createHash('sha256').update(file).digest('hex'),originalValid,reproduced,oldSignatureRejected,newSignatureValid});
}
if(!rows.length) throw Error('No KWZ files found; validation requires a nonempty corpus.');
const result={binary:path.resolve(binaryPath),binarySha256:crypto.createHash('sha256').update(binary).digest('hex'),privateKeyFileOffset:'0x283800',privateKeyVirtualAddress:'0x00383800',privateKeyDerBytes:1192,publicKeyFileOffset:'0x2836F0',publicKeyDerBytes:270,embeddedKeyPairMatches:true,keyBits:publicKey.asymmetricKeyDetails.modulusLength,algorithm:'RSASSA-PKCS1-v1_5 with SHA-256',files:rows.length,uniqueFiles:new Set(rows.map(row=>row.sha256)).size,allPassed:true,scope:'Cryptographic checks only; modified bodies are not written or tested in the game, and section CRCs are not updated for the one-bit mutation.',rows};
if(reportPath) fs.writeFileSync(reportPath,JSON.stringify(result,null,2)+'\n');
console.log(JSON.stringify({binary:result.binary,binarySha256:result.binarySha256,files:rows.length,originalSignaturesReproduced:rows.length,newSignaturesVerified:rows.length,keyBits:result.keyBits}));
