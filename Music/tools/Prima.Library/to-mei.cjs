// Engraves each MusicXML piece (.mxl) in a folder as MEI with the app's own Verovio, the way the app would: for the Library
// tool's prepare step (Prepare.cs). Run as: node to-mei.cjs <verovio-toolkit-wasm.js> <folder of .mxl> <folder for the .mei>
const fs = require('node:fs');
const path = require('node:path');
const [toolkit, from, to] = process.argv.slice(2);
const verovio = require(path.resolve(toolkit));
verovio.module.onRuntimeInitialized = () => {
    verovio.enableLog(verovio.LOG_OFF);
    const tk = new verovio.toolkit();
    tk.setOptions({ xmlIdChecksum: true });
    for (const name of fs.readdirSync(from).filter(n => n.endsWith('.mxl'))) {
        const bytes = fs.readFileSync(path.join(from, name));
        if (tk.loadZipDataBuffer(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength))) {
            fs.writeFileSync(path.join(to, name.replace(/\.mxl$/, '.mei')), tk.getMEI({ removeIds: false }));
        }
    }
};
