import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

// The owner, 17 November: "I can't even see the full titles ... especially when it's really, really long names." Titles were cut
// to one line with an ellipsis, and a sonata's movements differ only at their ends. These read Prima's stylesheet: the rules
// that draw a title in the Library's lists, and in a piece's bar upright, let it run onto as many lines as it takes.
const css = readFileSync(new URL("../../src/Prima.UI/wwwroot/prima.css", import.meta.url), "utf8").replace(/\/\*[\s\S]*?\*\//g, "");

// Every rule for exactly this selector, at the top level or inside a media query whose condition has `media` in it, in order.
function rules(selector, media = null) {
    const found = [];
    const pattern = /([^{}]+)\{([^{}]*)\}/g;
    for (const block of blocks()) {
        if ((media === null) !== (block.media === null) || (media !== null && !block.media.includes(media))) continue;
        for (const [, selectors, body] of block.text.matchAll(pattern)) {
            if (selectors.split(",").map(s => s.trim()).includes(selector)) found.push(body);
        }
    }
    return found;
}

// The stylesheet's top level, and each media query's rules, apart.
function blocks() {
    const parts = [];
    let top = "";
    let at = 0;
    for (const match of css.matchAll(/@media([^{]+)\{/g)) {
        top += css.slice(at, match.index);
        let depth = 1;
        let i = match.index + match[0].length;
        for (; depth > 0 && i < css.length; i++) depth += css[i] === "{" ? 1 : css[i] === "}" ? -1 : 0;
        parts.push({ media: match[1].trim(), text: css.slice(match.index + match[0].length, i - 1) });
        at = i;
    }
    top += css.slice(at);
    return [{ media: null, text: top }, ...parts];
}

// What the last rule to say it says of a property.
function last(bodies, property) {
    let value;
    for (const body of bodies) {
        for (const [, name, said] of body.matchAll(/([\w-]+)\s*:\s*([^;]+);?/g)) if (name === property) value = said.trim();
    }
    return value;
}

test("a title in the Library's lists runs onto as many lines as it takes", () => {
    const own = rules(".piece-item .piece-title");
    assert.ok(own.length > 0, "the Library's rows have a rule for their titles");
    assert.equal(last(own, "white-space"), "normal");
    assert.equal(last(own, "overflow"), "visible");
    assert.equal(last(own, "overflow-wrap"), "anywhere");
});

test("a piece's title in its bar upright is shown whole", () => {
    const upright = rules(".piece-bar-title", "orientation: portrait");
    assert.ok(upright.length > 0, "the bar has a rule for its title upright");
    for (const property of ["white-space", "text-overflow", "overflow"]) assert.equal(last(upright, property), undefined, `${property} isn't set upright`);
    assert.notEqual(last(rules(".piece-bar-title"), "white-space"), "nowrap");
});
