#!/usr/bin/env python3
"""Stand-in for org.freedesktop.ScreenSaver in the private headless KWin probe (KWin started without its own locker).
Lock() reports the screen locked to everyone, as KWin does on Plasma, so the probe can check nothing is sent then."""
from gi.repository import Gio, GLib

XML = '''<node><interface name="org.freedesktop.ScreenSaver">
<method name="GetActive"><arg type="b" direction="out"/></method>
<method name="Lock"/>
<signal name="ActiveChanged"><arg type="b"/></signal></interface></node>'''
active = False


def call(connection, sender, path, interface, method, parameters, invocation):
    global active
    if method == 'GetActive':
        invocation.return_value(GLib.Variant('(b)', (active,)))
    elif method == 'Lock':
        active = True
        connection.emit_signal(None, path, interface, 'ActiveChanged', GLib.Variant('(b)', (True,)))
        invocation.return_value(None)


bus = Gio.bus_get_sync(Gio.BusType.SESSION)
interface = Gio.DBusNodeInfo.new_for_xml(XML).interfaces[0]
bus.register_object('/ScreenSaver', interface, call, None, None)
Gio.bus_own_name_on_connection(bus, 'org.freedesktop.ScreenSaver', Gio.BusNameOwnerFlags.NONE, None, None)
GLib.MainLoop().run()
