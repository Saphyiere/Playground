# Verovio

`verovio-toolkit-wasm.js` is Verovio 6.3.0's single-file JavaScript toolkit (WebAssembly inside), unchanged, from the npm package
`verovio@6.3.0`. Verovio engraves Prima's sheet music. It's under the GNU Lesser General Public License, version 3 or later
(COPYING.LESSER, which builds on COPYING); its source is at https://github.com/rism-digital/verovio. To update it, replace the file
with the same file from a newer package.

The sheet sets the words in the music (lyrics, directions, the tempo, fingering) in the Liberation Serif the toolkit carries
(its `fontTextLiberation` option: Liberation 1.04, digitized data © 2007 Ascender Corporation, under the GNU General Public
License with the Liberation font exception), so they're drawn the width Verovio measured them at on any device. `Sheet.razor.js`
takes the four faces from the toolkit's own output when it starts and gives them to the page; no font file is added here.
