// ME Clicker helper: the narrow GNOME Shell bridge used by the Linux build of
// Minimum Effort Clicker on Wayland, where ordinary apps cannot read the pointer,
// see other windows, capture the screen or send input.
//
// Only programs whose executable path is listed in
// ~/.config/me-clicker-helper/allowed-clients may call it. Inputs a client holds
// are released when that client disconnects, so a crash never leaves a key down.

import Clutter from 'gi://Clutter';
import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import GdkPixbuf from 'gi://GdkPixbuf';
import Meta from 'gi://Meta';
import Shell from 'gi://Shell';

import * as Keyboard from 'resource:///org/gnome/shell/ui/status/keyboard.js';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';

Gio._promisify(Gio.DBusConnection.prototype, 'call');
Gio._promisify(Shell.Screenshot.prototype, 'screenshot_area');
Gio._promisify(Shell.Screenshot, 'composite_to_stream');

const BUS_NAME = 'io.github.MinimumEffortGames.ClickerHelper';
const OBJECT_PATH = '/io/github/MinimumEffortGames/ClickerHelper';
const ERROR_PREFIX = 'io.github.MinimumEffortGames.ClickerHelper.Error';
const API_VERSION = 1;
const MAX_CAPTURE_PIXELS = 32 * 1024 * 1024;

const INTERFACE = `
<node>
  <interface name="io.github.MinimumEffortGames.ClickerHelper1">
    <method name="GetVersion"><arg type="u" direction="out" name="version"/></method>
    <method name="GetPointer">
      <arg type="i" direction="out" name="x"/><arg type="i" direction="out" name="y"/>
    </method>
    <method name="GetMonitors"><arg type="a(iiiid)" direction="out" name="monitors"/></method>
    <method name="ListWindows"><arg type="a(tsi)" direction="out" name="windows"/></method>
    <method name="GetFocusWindow"><arg type="t" direction="out" name="id"/></method>
    <method name="GetWindow">
      <arg type="t" direction="in" name="id"/>
      <arg type="b" direction="out" name="exists"/><arg type="b" direction="out" name="minimized"/>
      <arg type="i" direction="out" name="pid"/><arg type="s" direction="out" name="title"/>
      <arg type="i" direction="out" name="x"/><arg type="i" direction="out" name="y"/>
      <arg type="i" direction="out" name="width"/><arg type="i" direction="out" name="height"/>
      <arg type="d" direction="out" name="scale"/>
    </method>
    <method name="QueryTarget">
      <arg type="t" direction="in" name="id"/>
      <arg type="t" direction="out" name="focus"/><arg type="b" direction="out" name="exists"/>
      <arg type="b" direction="out" name="minimized"/><arg type="i" direction="out" name="pid"/>
      <arg type="i" direction="out" name="x"/><arg type="i" direction="out" name="y"/>
      <arg type="i" direction="out" name="width"/><arg type="i" direction="out" name="height"/>
      <arg type="d" direction="out" name="scale"/>
      <arg type="i" direction="out" name="pointerX"/><arg type="i" direction="out" name="pointerY"/>
      <arg type="b" direction="out" name="shellReady"/>
    </method>
    <method name="WindowAt">
      <arg type="i" direction="in" name="x"/><arg type="i" direction="in" name="y"/>
      <arg type="s" direction="in" name="skipTitle"/>
      <arg type="t" direction="out" name="id"/><arg type="i" direction="out" name="pid"/>
    </method>
    <method name="GetKeyboardLayout"><arg type="s" direction="out" name="layout"/></method>
    <method name="GetShellState">
      <arg type="b" direction="out" name="inputReady"/><arg type="s" direction="out" name="detail"/>
    </method>
    <method name="ActivateWindow"><arg type="t" direction="in" name="id"/><arg type="b" direction="out" name="ok"/></method>
    <method name="PointerMove"><arg type="d" direction="in" name="x"/><arg type="d" direction="in" name="y"/></method>
    <method name="PointerButton"><arg type="u" direction="in" name="button"/><arg type="b" direction="in" name="pressed"/></method>
    <method name="Scroll"><arg type="u" direction="in" name="direction"/><arg type="u" direction="in" name="steps"/></method>
    <method name="Key"><arg type="u" direction="in" name="evdevCode"/><arg type="b" direction="in" name="pressed"/></method>
    <method name="Keyval"><arg type="u" direction="in" name="keyval"/><arg type="b" direction="in" name="pressed"/></method>
    <method name="ReleaseAll"/>
    <method name="CaptureRaw">
      <arg type="i" direction="in" name="x"/><arg type="i" direction="in" name="y"/>
      <arg type="i" direction="in" name="width"/><arg type="i" direction="in" name="height"/>
      <arg type="i" direction="out" name="outWidth"/><arg type="i" direction="out" name="outHeight"/>
      <arg type="i" direction="out" name="rowStride"/><arg type="i" direction="out" name="channels"/>
      <arg type="ay" direction="out" name="pixels"/>
    </method>
    <method name="CaptureWindowRaw">
      <arg type="t" direction="in" name="id"/>
      <arg type="i" direction="in" name="x"/><arg type="i" direction="in" name="y"/>
      <arg type="i" direction="in" name="width"/><arg type="i" direction="in" name="height"/>
      <arg type="i" direction="out" name="outWidth"/><arg type="i" direction="out" name="outHeight"/>
      <arg type="i" direction="out" name="rowStride"/><arg type="i" direction="out" name="channels"/>
      <arg type="ay" direction="out" name="pixels"/>
    </method>
    <method name="CapturePng">
      <arg type="i" direction="in" name="x"/><arg type="i" direction="in" name="y"/>
      <arg type="i" direction="in" name="width"/><arg type="i" direction="in" name="height"/>
      <arg type="ay" direction="out" name="png"/>
    </method>
    <method name="GrabAccelerator"><arg type="s" direction="in" name="accelerator"/><arg type="u" direction="out" name="action"/></method>
    <method name="UngrabAccelerator"><arg type="u" direction="in" name="action"/></method>
    <signal name="AcceleratorActivated"><arg type="u" name="action"/></signal>
    <signal name="AcceleratorDeactivated"><arg type="u" name="action"/></signal>
    <signal name="FocusChanged"><arg type="t" name="id"/></signal>
    <signal name="WindowClosed"><arg type="t" name="id"/></signal>
  </interface>
</node>`;

class AccessDenied extends Error {}

class Helper {
    constructor(settingsPath) {
        this._allowedPath = settingsPath;
        this._clients = new Map();
        this._accelerators = new Map();
        const seat = Clutter.get_default_backend().get_default_seat();
        this._pointer = seat.create_virtual_device(Clutter.InputDeviceType.POINTER_DEVICE);
        this._keyboard = seat.create_virtual_device(Clutter.InputDeviceType.KEYBOARD_DEVICE);
        this._dbus = Gio.DBusExportedObject.wrapJSObject(INTERFACE, this);
        this._dbus.export(Gio.DBus.session, OBJECT_PATH);
        this._ownName = Gio.DBus.session.own_name(BUS_NAME, Gio.BusNameOwnerFlags.NONE, null, null);
        this._focusId = global.display.connect('notify::focus-window', () =>
            this._emit('FocusChanged', '(t)', [this._id(global.display.focus_window)]));
        this._acceleratorId = global.display.connect('accelerator-activated', (_display, action) => {
            if (this._accelerators.has(action)) this._emit('AcceleratorActivated', '(u)', [action]);
        });
        this._acceleratorReleaseId = global.display.connect('accelerator-deactivated', (_display, action) => {
            if (this._accelerators.has(action)) this._emit('AcceleratorDeactivated', '(u)', [action]);
        });
        this._windowIds = new Map();
        this._createdId = global.display.connect('window-created', (_display, window) => this._track(window));
        for (const actor of global.get_window_actors()) this._track(actor.meta_window);
    }

    destroy() {
        for (const client of this._clients.values()) this._releaseClient(client);
        for (const [action, owner] of this._accelerators) this._ungrab(action);
        global.display.disconnect(this._focusId);
        global.display.disconnect(this._acceleratorId);
        global.display.disconnect(this._acceleratorReleaseId);
        global.display.disconnect(this._createdId);
        for (const [window, id] of this._windowIds) window.disconnect(id);
        this._windowIds.clear();
        Gio.DBus.session.unown_name(this._ownName);
        this._dbus.unexport();
        this._pointer = null;
        this._keyboard = null;
    }

    _track(window) {
        if (!window || this._windowIds.has(window)) return;
        const id = window.get_id();
        this._windowIds.set(window, window.connect('unmanaged', () => {
            this._windowIds.delete(window);
            this._emit('WindowClosed', '(t)', [id]);
        }));
    }

    _emit(name, signature, values) {
        try {
            this._dbus.emit_signal(name, new GLib.Variant(signature, values));
        } catch (e) {
            logError(e, 'ME Clicker helper signal');
        }
    }

    _id(window) {
        return window ? window.get_id() : 0;
    }

    _allowedClients() {
        try {
            const [, bytes] = GLib.file_get_contents(this._allowedPath);
            return new TextDecoder().decode(bytes).split('\n').map(l => l.trim()).filter(l => l.startsWith('/'));
        } catch {
            return [];
        }
    }

    async _client(invocation) {
        const sender = invocation.get_sender();
        let client = this._clients.get(sender);
        if (!client) {
            client = {sender, buttons: new Set(), keys: new Set(), keyvals: new Set(), allowed: this._check(sender, invocation.get_connection())};
            this._clients.set(sender, client);
            client.watch = Gio.DBus.session.watch_name(sender, Gio.BusNameWatcherFlags.NONE, null, () => {
                this._releaseClient(client);
                Gio.DBus.session.unwatch_name(client.watch);
                this._clients.delete(sender);
            });
        }
        if (!await client.allowed) throw new AccessDenied('This program is not on the ME Clicker helper allow list.');
        return client;
    }

    async _check(sender, connection) {
        try {
            const reply = await connection.call('org.freedesktop.DBus', '/org/freedesktop/DBus', 'org.freedesktop.DBus',
                'GetConnectionUnixProcessID', new GLib.Variant('(s)', [sender]), new GLib.VariantType('(u)'),
                Gio.DBusCallFlags.NONE, -1, null);
            const [pid] = reply.deepUnpack();
            const exe = GLib.file_read_link(`/proc/${pid}/exe`);
            return this._allowedClients().includes(exe);
        } catch (e) {
            logError(e, 'ME Clicker helper authorisation');
            return false;
        }
    }

    _releaseClient(client) {
        this._releaseInputs(client);
        for (const [action, owner] of [...this._accelerators])
            if (owner === client.sender) this._ungrab(action);
    }

    _releaseInputs(client) {
        const now = GLib.get_monotonic_time();
        for (const button of client.buttons)
            this._pointer?.notify_button(now, button, Clutter.ButtonState.RELEASED);
        for (const key of client.keys)
            this._keyboard?.notify_key(now, key, Clutter.KeyState.RELEASED);
        for (const keyval of client.keyvals)
            this._keyboard?.notify_keyval(now, keyval, Clutter.KeyState.RELEASED);
        client.buttons.clear();
        client.keys.clear();
        client.keyvals.clear();
    }

    _ungrab(action) {
        this._accelerators.delete(action);
        try {
            global.display.ungrab_accelerator(action);
        } catch (e) {
            logError(e, 'ME Clicker helper ungrab');
        }
    }

    _window(id) {
        return global.get_window_actors().map(a => a.meta_window).find(w => w && w.get_id() === id) ?? null;
    }

    // Each D-Bus method authorises its caller first, then returns a value or an error.
    _handle(invocation, work) {
        this._client(invocation).then(work).then(result => {
            invocation.return_value(result ?? null);
        }).catch(e => {
            const name = e instanceof AccessDenied ? 'AccessDenied' : 'Failed';
            if (!(e instanceof AccessDenied)) logError(e, 'ME Clicker helper');
            invocation.return_dbus_error(`${ERROR_PREFIX}.${name}`, e.message ?? String(e));
        });
    }

    GetVersionAsync(_params, invocation) {
        this._handle(invocation, () => new GLib.Variant('(u)', [API_VERSION]));
    }

    GetPointerAsync(_params, invocation) {
        this._handle(invocation, () => {
            const [x, y] = global.get_pointer();
            return new GLib.Variant('(ii)', [x, y]);
        });
    }

    GetMonitorsAsync(_params, invocation) {
        this._handle(invocation, () => new GLib.Variant('(a(iiiid))', [Main.layoutManager.monitors.map(m =>
            [m.x, m.y, m.width, m.height, m.geometry_scale ?? 1])]));
    }

    ListWindowsAsync(_params, invocation) {
        this._handle(invocation, () => {
            const windows = global.get_window_actors().map(a => a.meta_window)
                .filter(w => w && !w.skip_taskbar && w.get_window_type() === Meta.WindowType.NORMAL)
                .map(w => [w.get_id(), w.get_title() ?? '', w.get_pid()]);
            return new GLib.Variant('(a(tsi))', [windows]);
        });
    }

    GetFocusWindowAsync(_params, invocation) {
        this._handle(invocation, () => new GLib.Variant('(t)', [this._id(global.display.focus_window)]));
    }

    GetWindowAsync([id], invocation) {
        this._handle(invocation, () => {
            const window = this._window(id);
            if (!window) return new GLib.Variant('(bbisiiiid)', [false, false, 0, '', 0, 0, 0, 0, 1]);
            const client = window.frame_rect_to_client_rect(window.get_frame_rect());
            const monitor = window.get_monitor();
            const scale = monitor >= 0 ? global.display.get_monitor_scale(monitor) : 1;
            return new GLib.Variant('(bbisiiiid)', [true, window.minimized, window.get_pid(), window.get_title() ?? '',
                client.x, client.y, client.width, client.height, scale]);
        });
    }

    QueryTargetAsync([id], invocation) {
        this._handle(invocation, () => {
            const [px, py] = global.get_pointer();
            const focus = this._id(global.display.focus_window);
            const window = id ? this._window(id) : null;
            const ready = this._shellReady();
            if (!window) return new GLib.Variant('(tbbiiiiidiib)', [focus, false, false, 0, 0, 0, 0, 0, 1, px, py, ready]);
            const client = window.frame_rect_to_client_rect(window.get_frame_rect());
            const monitor = window.get_monitor();
            const scale = monitor >= 0 ? global.display.get_monitor_scale(monitor) : 1;
            return new GLib.Variant('(tbbiiiiidiib)', [focus, true, window.minimized, window.get_pid(),
                client.x, client.y, client.width, client.height, scale, px, py, ready]);
        });
    }

    // Input reaches app windows only in normal mode, with no overview, lock screen or modal dialog.
    _shellReady() {
        return !Main.overview.visible && Main.actionMode === Shell.ActionMode.NORMAL && Main.modalCount === 0;
    }

    GetShellStateAsync(_params, invocation) {
        this._handle(invocation, () => new GLib.Variant('(bs)', [this._shellReady(),
            `overview=${Main.overview.visible} mode=${Main.actionMode} modal=${Main.modalCount} keyFocus=${global.stage.key_focus}`]));
    }

    GetKeyboardLayoutAsync(_params, invocation) {
        this._handle(invocation, () => {
            const source = Keyboard.getInputSourceManager().currentSource;
            return new GLib.Variant('(s)', [source ? `${source.type}:${source.id}` : '']);
        });
    }

    WindowAtAsync([x, y, skipTitle], invocation) {
        this._handle(invocation, () => {
            const actors = global.get_window_actors();
            for (let i = actors.length - 1; i >= 0; i--) {
                const window = actors[i].meta_window;
                if (!window || window.minimized || !window.showing_on_its_workspace() || !actors[i].visible) continue;
                if (skipTitle && window.get_title() === skipTitle) continue;
                const rect = window.get_frame_rect();
                if (x >= rect.x && y >= rect.y && x < rect.x + rect.width && y < rect.y + rect.height)
                    return new GLib.Variant('(ti)', [window.get_id(), window.get_pid()]);
            }
            return new GLib.Variant('(ti)', [0, 0]);
        });
    }

    ActivateWindowAsync([id], invocation) {
        this._handle(invocation, () => {
            const window = this._window(id);
            if (window) Main.activateWindow(window);
            return new GLib.Variant('(b)', [window !== null]);
        });
    }

    PointerMoveAsync([x, y], invocation) {
        this._handle(invocation, () => {
            this._pointer.notify_absolute_motion(GLib.get_monotonic_time(), x, y);
        });
    }

    PointerButtonAsync([button, pressed], invocation) {
        this._handle(invocation, client => {
            if (![1, 2, 3, 8, 9].includes(button)) throw new Error('Unsupported pointer button.');
            this._pointer.notify_button(GLib.get_monotonic_time(), button,
                pressed ? Clutter.ButtonState.PRESSED : Clutter.ButtonState.RELEASED);
            if (pressed) client.buttons.add(button); else client.buttons.delete(button);
        });
    }

    ScrollAsync([direction, steps], invocation) {
        this._handle(invocation, () => {
            const directions = [Clutter.ScrollDirection.UP, Clutter.ScrollDirection.DOWN,
                Clutter.ScrollDirection.LEFT, Clutter.ScrollDirection.RIGHT];
            if (direction >= directions.length || steps < 1 || steps > 100) throw new Error('Unsupported scroll.');
            for (let i = 0; i < steps; i++)
                this._pointer.notify_discrete_scroll(GLib.get_monotonic_time(), directions[direction], Clutter.ScrollSource.WHEEL);
        });
    }

    KeyAsync([code, pressed], invocation) {
        this._handle(invocation, client => {
            if (code < 1 || code > 0x2ff) throw new Error('Unsupported key code.');
            this._keyboard.notify_key(GLib.get_monotonic_time(), code,
                pressed ? Clutter.KeyState.PRESSED : Clutter.KeyState.RELEASED);
            if (pressed) client.keys.add(code); else client.keys.delete(code);
        });
    }

    KeyvalAsync([keyval, pressed], invocation) {
        this._handle(invocation, client => {
            if (keyval < 0x20 || keyval > 0x110ffff) throw new Error('Unsupported key value.');
            this._keyboard.notify_keyval(GLib.get_monotonic_time(), keyval,
                pressed ? Clutter.KeyState.PRESSED : Clutter.KeyState.RELEASED);
            if (pressed) client.keyvals.add(keyval); else client.keyvals.delete(keyval);
        });
    }

    ReleaseAllAsync(_params, invocation) {
        this._handle(invocation, client => this._releaseInputs(client));
    }

    async _capture(x, y, width, height) {
        if (width < 1 || height < 1 || width * height > MAX_CAPTURE_PIXELS) throw new Error('Capture area is empty or too large.');
        const stream = Gio.MemoryOutputStream.new_resizable();
        await new Shell.Screenshot().screenshot_area(x, y, width, height, stream);
        stream.close(null);
        return stream.steal_as_bytes();
    }

    CaptureRawAsync([x, y, width, height], invocation) {
        this._handle(invocation, async () => {
            const png = await this._capture(x, y, width, height);
            const pixbuf = GdkPixbuf.Pixbuf.new_from_stream(Gio.MemoryInputStream.new_from_bytes(png), null);
            return new GLib.Variant('(iiiiay)', [pixbuf.get_width(), pixbuf.get_height(), pixbuf.get_rowstride(),
                pixbuf.get_n_channels(), pixbuf.get_pixels()]);
        });
    }

    // One window's own pixels, like Windows Graphics Capture: overlays and other windows never cover it.
    // x and y are relative to the window's client area.
    CaptureWindowRawAsync([id, x, y, width, height], invocation) {
        this._handle(invocation, async () => {
            if (width < 1 || height < 1 || width * height > MAX_CAPTURE_PIXELS) throw new Error('Capture area is empty or too large.');
            const window = this._window(id);
            const actor = window?.get_compositor_private();
            if (!window || !actor || window.minimized) throw new Error('The window is closed or minimized.');
            const buffer = window.get_buffer_rect();
            const client = window.frame_rect_to_client_rect(window.get_frame_rect());
            const scale = actor.get_resource_scale();
            const texture = actor.paint_to_content(null)?.get_texture();
            if (!texture) throw new Error('The window has no content to capture yet.');
            const stream = Gio.MemoryOutputStream.new_resizable();
            const pixbuf = await Shell.Screenshot.composite_to_stream(texture,
                Math.round((client.x - buffer.x + x) * scale), Math.round((client.y - buffer.y + y) * scale),
                Math.round(width * scale), Math.round(height * scale), scale, null, 0, 0, 1, stream);
            stream.close(null);
            return new GLib.Variant('(iiiiay)', [pixbuf.get_width(), pixbuf.get_height(), pixbuf.get_rowstride(),
                pixbuf.get_n_channels(), pixbuf.get_pixels()]);
        });
    }

    CapturePngAsync([x, y, width, height], invocation) {
        this._handle(invocation, async () => {
            const png = await this._capture(x, y, width, height);
            return new GLib.Variant('(ay)', [png.toArray()]);
        });
    }

    GrabAcceleratorAsync([accelerator], invocation) {
        this._handle(invocation, client => {
            const action = global.display.grab_accelerator(accelerator, Meta.KeyBindingFlags.TRIGGER_RELEASE ?? Meta.KeyBindingFlags.NONE);
            if (action === Meta.KeyBindingAction.NONE) throw new Error(`${accelerator} is already used by another shortcut.`);
            Main.wm.allowKeybinding(Meta.external_binding_name_for_action(action), Shell.ActionMode.ALL);
            this._accelerators.set(action, client.sender);
            return new GLib.Variant('(u)', [action]);
        });
    }

    UngrabAcceleratorAsync([action], invocation) {
        this._handle(invocation, client => {
            if (this._accelerators.get(action) === client.sender) this._ungrab(action);
        });
    }
}

export default class ClickerHelperExtension extends Extension {
    enable() {
        const path = GLib.build_filenamev([GLib.get_user_config_dir(), 'me-clicker-helper', 'allowed-clients']);
        this._helper = new Helper(path);
    }

    disable() {
        this._helper?.destroy();
        this._helper = null;
    }
}
