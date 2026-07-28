# TheToolShed
The Toolshed Component & Tool liberty for Railroader in Conjunction with FUSE

## Service Loader Documentation

- `Examples/service-facility-setup-guide.md` gives the overall service-loader setup flow.
- `Examples/service-loader-component-blueprint.md` lists every `ToolshedServiceStorage` and `ToolshedServiceLoadPoint` editor setting in component order, with recommended values for coal, water, diesel, bunker-C, and wood loaders.

## RailLoader Builds

- `dist/Toolshed-FUSE.zip` contains the FUSE edition.
- `dist/Toolshed-RailLoader.zip` contains the RailLoader edition.

Both editions include the one-time configuration scan fix and do not walk the
complete `Mods` tree every two seconds when no matching definitions exist.
