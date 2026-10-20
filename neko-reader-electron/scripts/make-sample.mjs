/* Gera samples/exemplo.cbz com 6 paginas PNG validas + ComicInfo.xml. */
import { createRequire } from 'module';
import fs from 'fs';
const require = createRequire(import.meta.url);
const AdmZip = require('adm-zip');
const zlib = require('zlib');

function crc32(buf){let t=[],c;for(let n=0;n<256;n++){c=n;for(let k=0;k<8;k++)c=c&1?0xEDB88320^(c>>>1):c>>>1;t[n]=c;}let r=~0;for(const b of buf)r=(r>>>8)^t[(r^b)&0xff];return ~r>>>0;}
function chunk(type,data){const len=Buffer.alloc(4);len.writeUInt32BE(data.length);const td=Buffer.concat([Buffer.from(type),data]);const c=Buffer.alloc(4);c.writeUInt32BE(crc32(td));return Buffer.concat([len,td,c]);}
function png(w,h,rgb){
  const ihdr=Buffer.alloc(13);ihdr.writeUInt32BE(w,0);ihdr.writeUInt32BE(h,4);ihdr[8]=8;ihdr[9]=2;
  const raw=Buffer.alloc(h*(1+w*3));
  for(let y=0;y<h;y++){const off=y*(1+w*3);raw[off]=0;for(let x=0;x<w;x++){const p=off+1+x*3;raw[p]=rgb[0];raw[p+1]=rgb[1];raw[p+2]=rgb[2];}}
  return Buffer.concat([Buffer.from([0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A]),chunk('IHDR',ihdr),chunk('IDAT',zlib.deflateSync(raw)),chunk('IEND',Buffer.alloc(0))]);
}
const zip=new AdmZip();
const colors=[[30,60,120],[40,90,150],[50,120,170],[60,150,190],[80,180,200],[110,210,220]];
colors.forEach((c,i)=>zip.addFile(String(i+1).padStart(3,'0')+'.png',png(600,850,c)));
zip.addFile('ComicInfo.xml',Buffer.from('<ComicInfo><Title>Exemplo NekoReader</Title><Series>Samples</Series><Number>1</Number><Manga>No</Manga></ComicInfo>','utf8'));
fs.mkdirSync('samples',{recursive:true});
zip.writeZip('samples/exemplo.cbz');
console.log('OK: samples/exemplo.cbz criado com '+colors.length+' paginas.');
