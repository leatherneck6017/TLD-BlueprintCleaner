# Changelog

Dates use the dd-mmm-yy format.

## Unreleased

### Changed

- Blueprints that craft the same result are now stacked together even when they are crafted at different crafting locations (workbench, forge, ammo workbench, fire, anywhere, furniture workbench). This replaces the old "Blueprints with different crafting location can't be stacked" known issue.
- In a crafting location view, a stack only counts and cycles the blueprints that view lists. The *All* view stacks every location together.
- The blueprint a stack shows is always one the current view lists: your current pick if it is listed there, otherwise one craftable at your current crafting location, otherwise the first one listed. A stack never jumps to another location view when you cycle it.
- The `[n/m]` label counts the blueprints the current view lists, and adds the crafting location when a stack mixes locations, e.g. `[2/4 · Forge]`. Stacks from a single location keep the plain `[n/m]`, and the label is no longer shown when only one blueprint of a stack is listed.

### Added

- **Debug logging** mod setting (off by default) that writes stacking, cycling and scroll details to the MelonLoader log.

### Fixed

- Cycling a stack with the arrow keys no longer scrolls the selected blueprint to the bottom of the list. The list also keeps its position after hiding or showing a blueprint.
- Showing a hidden decoration blueprint again no longer leaves its stack blank.

### Notes

- The hidden blueprints file (`Mods/RemovedBlueprints_dont_touch_this_file.json`) is unchanged and keeps working.

## Game update and SC+ fix - 21-Nov-26

Submitted upstream as a pull request (not merged yet).

### Changed

- Updated the references for the current game version and MelonLoader. The csproj now uses `$(MelonLoaderPath)` instead of a hard-coded Steam path.

### Fixed

- Fixed a NullReferenceException in the cooking/crafting list click handler when used with SC+ (scene check now uses `GameManager.m_ActiveScene`).
- Added null guards: arrow keys do nothing when no blueprint is selected, and the blueprint name label no longer fails on empty names or on decoration blueprints.

### Removed

- Unused fields and commented-out log code.

## 1.1.1 - 07-Feb-26

- Last upstream release by RomainDeschampsFR. See the [upstream repository](https://github.com/RomainDeschampsFR/BlueprintCleaner) for earlier history.
