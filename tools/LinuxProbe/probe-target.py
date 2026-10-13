#!/usr/bin/env python3
"""Harmless GTK 4 receiver for the headless Linux probe. Logs clicks, keys and scrolls to a file."""
import sys
import time
import gi
gi.require_version('Gtk', '4.0')
from gi.repository import Gtk, Gdk

_file = open(sys.argv[1], 'a', buffering=1)


class _Log:
    def write(self, text):
        _file.write(f'{time.time():.3f} {text}')


log = _Log()
app = Gtk.Application(application_id='io.github.MinimumEffortGames.ProbeTarget')


def activate(application):
    window = Gtk.ApplicationWindow(application=application, title='MEC Probe Target')
    window.set_default_size(640, 420)
    css = Gtk.CssProvider()
    css.load_from_data(b'window, label { background: #3366cc; color: #ffffff; }')
    Gtk.StyleContext.add_provider_for_display(Gdk.Display.get_default(), css, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)
    window.set_child(Gtk.Label(label='MEC probe target'))
    click = Gtk.GestureClick(button=0)
    click.connect('pressed', lambda g, n, x, y: log.write(f'press button={g.get_current_button()} n={n} x={x:.0f} y={y:.0f}\n'))
    click.connect('released', lambda g, n, x, y: log.write(f'release button={g.get_current_button()} x={x:.0f} y={y:.0f}\n'))
    window.add_controller(click)
    keys = Gtk.EventControllerKey()
    keys.connect('key-pressed', lambda c, keyval, code, state: log.write(f'key down evdev={code - 8} keyval={Gdk.keyval_name(keyval)}\n') or False)
    keys.connect('key-released', lambda c, keyval, code, state: log.write(f'key up evdev={code - 8}\n'))
    window.add_controller(keys)
    scroll = Gtk.EventControllerScroll(flags=Gtk.EventControllerScrollFlags.BOTH_AXES)
    scroll.connect('scroll', lambda c, dx, dy: log.write(f'scroll dx={dx} dy={dy}\n') or True)
    window.add_controller(scroll)
    window.connect('notify::is-active', lambda w, _p: log.write(f'active={w.is_active()}\n'))
    motion = Gtk.EventControllerMotion()
    motion.connect('enter', lambda c, x, y: log.write(f'enter x={x:.0f} y={y:.0f}\n'))
    motion.connect('leave', lambda c: log.write('leave\n'))
    window.add_controller(motion)
    window.present()
    log.write('ready\n')


app.connect('activate', activate)
app.run([])
