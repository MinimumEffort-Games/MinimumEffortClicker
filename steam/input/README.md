# Steam Input layout

The Steam builds ship this folder as `steam_input/`. Steamworks > Steam Input > Default Controller Configuration is
set to Custom Configuration with the path `steam_input/steam_input_manifest.vdf`, so a Steam Deck starts the clicker
on `controller_neptune.vdf`.

That layout is Valve's Web Browser template for the Steam Deck (`controller_base/templates/controller_neptune_webbrowser.vdf`
in a Steam install) with View, L4 and R4 unbound; L5 and R5 are unbound in the template already. The clicker reads the
real controller itself, beside Steam, so a button that Steam also turned into a key or click would count twice. Those
five buttons carry the clicker's Steam Deck shortcuts. The trackpads, triggers and touchscreen still move and click
the mouse, so the clicker's own window stays usable in Game Mode.

Only the Steam Deck is listed. Other controllers keep whatever Steam gives them; the clicker reads them directly too.
Players can still pick another layout in Steam.
