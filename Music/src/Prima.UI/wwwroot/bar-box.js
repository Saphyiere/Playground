// Where a bar of Verovio's drawing is on the page, the one way every part of the sheet measures it: a tap finding the bar under the
// finger, the chosen bar or loop lit on the music (bars.js), a bar outlined, the cursor crossing a bar line, a reading's bars.
//
// Across, a bar is as wide as its staff lines: from its bar line to the next (a line's first bar from where the line starts, its
// clef and signatures in it, and its number printed over it, which can stand a little out to the left). Up and down, from its top
// staff's top to its lowest staff's bottom, the notes over and under them included, and an octave line (8va, 8vb) printed over
// or under it, which says where those notes are played.
//
// Never by the box of everything drawn with the bar: Verovio draws a mark that runs on over bar lines (an octave line, a slur, a
// hairpin, a pedal line, a trill's line, a tie) with the bar it starts in, so that box runs on over the bars after it, and a bar
// measured by it was chosen for them too: under an 8va from bar 21 to 24, a tap on bar 22 chose bar 21 and lit 21 to 24.
//
// In the page's px, as getBoundingClientRect has them, or null for a bar with no staff drawn. Every bar of a long piece is
// measured each time the choice changes, so as little is asked of the page as will do: one staff line (they all run the bar's
// width), and the line's octave lines by their class (the page keeps that list).
export function barBox(measure) {
    const staves = [...measure.querySelectorAll(":scope > .staff")].map(s => s.getBoundingClientRect());
    if (staves.length === 0) return null;
    const line = measure.querySelector(":scope > .staff > path")?.getBoundingClientRect();
    const number = measure.querySelector(":scope > .mNum")?.getBoundingClientRect();
    const across = [...(line ? [line] : staves), ...(number?.width > 0 ? [number] : [])];
    const left = Math.min(...across.map(r => r.left)), right = Math.max(...across.map(r => r.right));
    // An octave line is drawn with the bar it starts in, and carried on to the next line by that line: any of its line's over
    // this bar.
    const octaves = [...(measure.closest(".system")?.getElementsByClassName("octave") ?? [])].map(o => o.getBoundingClientRect())
        .filter(r => r.width > 0 && r.left < right && r.right > left);
    const tall = [...staves, ...octaves];
    return { left, right, top: Math.min(...tall.map(r => r.top)), bottom: Math.max(...tall.map(r => r.bottom)) };
}
