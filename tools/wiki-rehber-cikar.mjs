// ROB RUN Türkçe Wiki'nin app.js dosyasındaki rehber içeriklerini (başlangıç rehberi, para yöntemleri,
// sık yapılan hatalar, birikimli cihazlar, başlangıç paketi) JSON olarak çıkarır.
// Yalnızca adı verilen dizi/nesne değişmezleri (literal) izole bir vm bağlamında değerlendirilir;
// sitenin kodu çalıştırılmaz, DOM ya da ağ erişimi yoktur.
//
// Kullanım: node tools/wiki-rehber-cikar.mjs <app.js> <cikti.json>
import fs from 'node:fs';
import vm from 'node:vm';

const [, , src, out] = process.argv;
if (!src || !out) { console.error('Kullanım: node wiki-rehber-cikar.mjs <app.js> <cikti.json>'); process.exit(1); }
const code = fs.readFileSync(src, 'utf8');

/** "const ad = [ ... ]" ya da "ad = { ... }" değişmezini parantez eşleyerek bulur (metin ve yorumları atlar). */
function literal(name) {
  const re = new RegExp(`(?:const|let|var)?\\s*\\b${name}\\s*=\\s*([\\[{])`);
  const m = re.exec(code);
  if (!m) throw new Error(`${name} bulunamadı`);
  let i = m.index + m[0].length - 1, depth = 0, q = null;
  const start = i;
  for (; i < code.length; i++) {
    const c = code[i];
    if (q) {
      if (c === '\\') { i++; continue; }
      if (c === q) q = null;
      continue;
    }
    if (c === '"' || c === "'" || c === '`') { q = c; continue; }
    if (c === '/' && code[i + 1] === '/') { i = code.indexOf('\n', i); continue; }
    if (c === '/' && code[i + 1] === '*') { i = code.indexOf('*/', i) + 1; continue; }
    if (c === '[' || c === '{') depth++;
    else if (c === ']' || c === '}') { depth--; if (depth === 0) break; }
  }
  const text = code.slice(start, i + 1);
  if (text.includes('${')) throw new Error(`${name} şablon ifadesi içeriyor; elle bakılmalı`);
  // izole bağlam: yalnızca değişmez değerlendirilir
  return vm.runInNewContext('(' + text + ')', Object.create(null), { timeout: 1000 });
}

const result = {
  baslangicAdimlari: literal('steps6'),
  paraYontemleri: literal('moneyMethods'),
  hatalar: literal('mistakes6'),
  birikimliCihazlar: literal('devices'),
  birikimliKisa: literal('CUMULATIVE_SHORT_META'),
  baslangicPaketi: (() => { const m = /STARTER_PACK_NODE_IDS\s*=\s*(\[[^\]]*\])/.exec(code); return m ? vm.runInNewContext(m[1], Object.create(null)) : []; })(),
};
fs.writeFileSync(out, JSON.stringify(result, null, 1), 'utf8');
console.log(Object.entries(result).map(([k, v]) => `${k}: ${Array.isArray(v) ? v.length : Object.keys(v).length}`).join(', '));
