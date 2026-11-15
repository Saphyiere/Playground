// What goes wrong on the page, kept until Prima writes it in its problem log (Settings › Problems). When .NET fails in a way that
// stops the page answering, Blazor says why on the console and shows its error bar, and from then on the page can't reach .NET:
// so what it said is kept in local storage, which outlives Reload and Prima being closed, and MainLayout takes it in the next time
// Prima starts (primaProblems.take). Script errors and promises nobody caught are kept the same way. Loaded before Blazor's own
// script, so it hears everything Blazor says.
(() => {
    const key = "prima.problems";
    const read = () => {
        try { return JSON.parse(localStorage.getItem(key) ?? "[]"); } catch { return []; }
    };
    const write = list => {
        try { localStorage.setItem(key, JSON.stringify(list.slice(-20))); } catch { }
    };
    const text = value => {
        if (value instanceof Error) return value.stack ?? value.message;
        if (typeof value === "string") return value;
        try { return JSON.stringify(value); } catch { return String(value); }
    };
    // A call back into a page that has already gone (the music redrawn as it was left): nothing went wrong.
    const harmless = said => /There is no tracked object with id|JSDisconnectedException/.test(said);
    const keep = (kind, said) => {
        said = String(said ?? "").slice(0, 16000);
        if (!said || harmless(said)) return;
        const list = read();
        list.push({ at: new Date().toISOString(), kind, path: location.pathname + location.search, text: said });
        write(list);
    };

    const say = console.error.bind(console);
    console.error = (...args) => {
        keep("console", args.map(text).join(" "));
        say(...args);
    };
    addEventListener("error", e => keep("script", e.error ? text(e.error) : `${e.message} (${e.filename}:${e.lineno})`));
    addEventListener("unhandledrejection", e => keep("promise", text(e.reason)));

    // Blazor's error bar shown: the page has stopped answering, for what it said last. That's kept as why, and the bar says it.
    const stopped = bar => {
        if (bar.style.display !== "block" || bar.dataset.kept) return;
        bar.dataset.kept = "true";
        const list = read();
        const last = list.at(-1);
        if (!last) return;
        last.kind = "stopped";
        write(list);
        const what = bar.querySelector(".problem-what");
        if (what) what.textContent = last.text.split("\n")[0];
    };
    const watch = () => {
        const bar = document.getElementById("blazor-error-ui");
        if (bar) new MutationObserver(() => stopped(bar)).observe(bar, { attributes: true, attributeFilter: ["style"] });
    };
    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", watch);
    else watch();

    window.primaProblems = {
        take() {
            const list = read();
            try { localStorage.removeItem(key); } catch { }
            return JSON.stringify(list);
        },
    };
})();
