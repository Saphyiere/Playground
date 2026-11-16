// Engraves each MusicXML piece (.mxl) in a folder as MEI with the app's own Verovio, the way the app would: for the Library
// tool's prepare step (Prepare.cs). With "mei" after the folders it reads the MEI there instead (the local collection's hymns,
// which abc2svg wrote) and writes it again as Verovio makes it, which is what the app reads. Run as:
//   node to-mei.cjs <verovio-toolkit-wasm.js> <folder of .mxl> <folder for the .mei> [mxl|mei]
const fs = require('node:fs');
const path = require('node:path');
const [toolkit, from, to, kind = 'mxl'] = process.argv.slice(2);
const verovio = require(path.resolve(toolkit));
verovio.module.onRuntimeInitialized = () => {
    verovio.enableLog(verovio.LOG_OFF);
    const tk = new verovio.toolkit();
    tk.setOptions({ xmlIdChecksum: true });
    for (const name of fs.readdirSync(from).filter(n => n.endsWith(`.${kind}`))) {
        const file = path.join(from, name);
        const bytes = fs.readFileSync(file);
        const loaded = kind === 'mei'
            ? tk.loadData(bytes.toString('utf8'))
            : tk.loadZipDataBuffer(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength));
        if (loaded) {
            fs.writeFileSync(path.join(to, name.replace(/\.(mxl|mei)$/, '.mei')), tk.getMEI({ removeIds: false }));
        }
    }
};
