import { existsSync, readFileSync } from "node:fs";

// A module of Prima.UI's as one script for a vm context: each module it imports by a relative path to a .js file put in place of
// its import line, once, as the browser has it loaded first. The `export`s, its own and theirs, are left for the test to take
// off, as it does for a module that imports nothing. Not a test file: the tests import it.
export function script(url, seen = new Set()) {
    return readFileSync(url, "utf8").replace(/^import\s*\{[^}]*\}\s*from\s*"(\.{1,2}\/[^"]+\.js)";?\r?$/gm, (_, path) => {
        const at = onDisk(new URL(path, new URL(served(url), "https://app/")).pathname.slice(1));
        if (seen.has(at.href)) return "";
        seen.add(at.href);
        return script(at, seen);
    });
}

// The app serves Prima.UI's wwwroot and a component's own script (Components/Sheet.razor.js) from one folder, so an import's
// path is from where the importing file is served: Sheet.razor.js's "../bar-box.js" is wwwroot/bar-box.js on disk.
const root = new URL("../../src/Prima.UI/", import.meta.url);
const served = url => url.href.slice(root.href.length).replace(/^wwwroot\//, "");
const onDisk = path => existsSync(new URL(`wwwroot/${path}`, root)) ? new URL(`wwwroot/${path}`, root) : new URL(path, root);
