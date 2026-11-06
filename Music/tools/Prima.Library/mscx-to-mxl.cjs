// Converts one MuseScore file (.mscx) to compressed MusicXML (.mxl) with webmscore, MuseScore's engine built for the web, for the
// Library tool's local collection (Dcml.cs). Its harmony labels go first: they're the DCML's analyses, and would print as chord
// symbols. So do the instrument's names, which would print as "Piano" beside the first system and "Pno." beside the rest. One
// file a run, since after some files webmscore's memory is left broken and every file after it fails. Run as:
//   node mscx-to-mxl.cjs <node_modules with webmscore> <in.mscx> <out.mxl>
const fs = require('node:fs');
const path = require('node:path');

const [modules, from, to] = process.argv.slice(2);

// Its loader mistakes Node for a browser by Node's own navigator and fetch: the first can't be set, and the second can't read
// the file path it's given for the WebAssembly. With fetch gone it reads the file itself.
Object.defineProperty(globalThis, 'navigator', { value: {}, writable: true, configurable: true });
const fetchWas = globalThis.fetch;
globalThis.fetch = undefined;
const WebMscore = require(path.join(modules, 'webmscore'));
globalThis.fetch = fetchWas;

WebMscore.ready.then(async () => {
    const text = fs.readFileSync(from, 'utf8').replace(/<Harmony>[\s\S]*?<\/Harmony>/g, '').replace(/<(longName|shortName)>[^<]*<\/\1>/g, '');
    const score = await WebMscore.load('mscx', new TextEncoder().encode(text), [], true);
    fs.writeFileSync(to, await score.saveMxl());
    score.destroy();
    process.exit(0);
}).catch(e => {
    console.error(String(e && e.message || e));
    process.exit(1);
});
