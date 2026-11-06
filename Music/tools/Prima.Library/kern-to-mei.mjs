// Engraves each Humdrum file (.krn) in a folder as MEI, for the Library tool's local collection (Prepare.cs). The app's own copy
// of Verovio leaves Humdrum out, so this uses the build that reads it, from the npm package of the same version (Program.cs
// installs it). The MEI it writes is what the app's copy reads, without the instrument's name ("Piano" beside the first system).
// Run as:
//   node kern-to-mei.mjs <node_modules with verovio> <folder of .krn> <folder for the .mei>
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const [modules, from, to] = process.argv.slice(2);
const dist = path.join(modules, 'verovio', 'dist');
const { default: createVerovioModule } = await import(pathToFileURL(path.join(dist, 'verovio-module-hum.mjs')).href);
const { VerovioToolkit } = await import(pathToFileURL(path.join(dist, 'verovio.mjs')).href);
const tk = new VerovioToolkit(await createVerovioModule());
tk.setOptions({ xmlIdChecksum: true });
for (const name of fs.readdirSync(from).filter(n => n.endsWith('.krn'))) {
    if (tk.loadData(fs.readFileSync(path.join(from, name), 'utf8'))) {
        const mei = tk.getMEI({ removeIds: false }).replace(/<(label|labelAbbr)\b[^>]*>[\s\S]*?<\/\1>/g, '');
        fs.writeFileSync(path.join(to, name.replace(/\.krn$/, '.mei')), mei);
    }
}
