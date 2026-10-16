**What changes for the player, and why?**

**How was it verified?** `scripts/Test.ps1` (Windows) and `scripts/linux/test.sh Release` (Linux) results, and what you tried in the running app on each.

**Checklist**
- [ ] Core and Engine still have no WPF, Avalonia or native dependency (the layering test passes).
- [ ] Nothing starts playback on load, import or focus restore; Stop All still works.
- [ ] Saved data from the previous version still loads (schema bumped and migrated if the format changed).
- [ ] No owner data, screenshots of other people, credentials or generated binaries in the diff.
- [ ] Lock files and the licence inventory updated if a dependency changed.
