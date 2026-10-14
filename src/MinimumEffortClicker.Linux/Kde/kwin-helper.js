// ME Clicker KWin helper: loaded into KWin by the Linux build of Minimum Effort Clicker on KDE Plasma, where an app
// cannot see other windows or read the pointer by itself. It only answers the clicker that loaded it (its unique
// D-Bus name is written in below), only reads window state and only brings windows to the front.
// Questions arrive as the answer to "Next"; each call to "Next" carries the previous answer, so one call is in
// flight at a time. Focus changes and closed windows are reported through "Event".

const SERVICE = "%SERVICE%";
const PATH = "/io/github/MinimumEffortGames/ClickerKWin";
const IFACE = "io.github.MinimumEffortGames.ClickerKWin1";

function key(w) { return w ? String(w.internalId) : ""; }
function find(id) { return id ? workspace.windowList().find(w => key(w) === id) || null : null; }
function scale(w) { return w.output ? w.output.devicePixelRatio : 1; }
function state(w) {
    if (!w) return null;
    const g = w.clientGeometry;
    return { i: key(w), m: w.minimized, p: w.pid, t: w.caption || "", x: Math.round(g.x), y: Math.round(g.y),
        w: Math.round(g.width), h: Math.round(g.height), s: scale(w) };
}
function pointer() { return [Math.floor(workspace.cursorPos.x), Math.floor(workspace.cursorPos.y)]; }

const ops = {
    pointer: () => pointer(),
    monitors: () => workspace.screens.map(s => [s.geometry.x, s.geometry.y, s.geometry.width, s.geometry.height, s.devicePixelRatio]),
    windows: () => workspace.windowList().filter(w => w.normalWindow && !w.skipTaskbar).map(w => [key(w), w.caption || "", w.pid]),
    focus: () => key(workspace.activeWindow),
    window: c => state(find(c.i)),
    target: c => ({ w: state(find(c.i)), p: pointer() }),
    at: c => {
        for (const w of workspace.windowAt({ x: c.x, y: c.y }, 64)) {
            if (w.minimized || (c.k && w.caption === c.k)) continue;
            return [key(w), w.pid];
        }
        return ["", 0];
    },
    activate: c => {
        const w = find(c.i);
        if (!w) return false;
        w.minimized = false;
        workspace.activeWindow = w;
        return true;
    }
};

let running = true;
function answer(text) {
    let command;
    try { command = JSON.parse(text); } catch (e) { return ""; }
    if (command.o === "idle") return "";
    if (command.o === "quit") { running = false; return ""; }
    try { return JSON.stringify({ s: command.s, r: ops[command.o](command) }); }
    catch (e) { return JSON.stringify({ s: command.s, e: String(e) }); }
}
function poll(previous) {
    callDBus(SERVICE, PATH, IFACE, "Next", previous, function (command) {
        // No string means the clicker is gone or failed: stop instead of asking again in a loop.
        if (running && typeof command === "string") poll(answer(command));
    });
}

workspace.windowActivated.connect(w => { if (running) callDBus(SERVICE, PATH, IFACE, "Event", "f " + key(w)); });
workspace.windowRemoved.connect(w => { if (running) callDBus(SERVICE, PATH, IFACE, "Event", "c " + key(w)); });
poll("");
