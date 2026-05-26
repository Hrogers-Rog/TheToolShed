# Toolshed Service Facility Test (FUSE)

FUSE equivalent of the temporary RailLoader service-facility test pack.

Use this when switching from RailLoader placement testing to FUSE validation. It places the same Dillsboro bunker-c loader and wood shed with FUSE `world.scenery`, creates the same storage/interchange operations, and uses the same `ToolshedServiceFacilities.json` runtime service binding.

Expected installed layout:

```text
Railroader/Mods/Toolshed/Info.json
Railroader/Mods/Toolshed/Toolshed.dll
Railroader/Mods/Toolshed/SCAssetPacks/...
Railroader/Mods/FUSE/...
Railroader/Mods/Toolshed Service Facility Test (FUSE)/Info.json
Railroader/Mods/Toolshed Service Facility Test (FUSE)/service-facility-test.fuse.json
Railroader/Mods/Toolshed Service Facility Test (FUSE)/ToolshedServiceFacilities.json
```

The FUSE test pack's `Info.json` only declares `FUSE` as a dependency because FUSE package ordering can only target FUSE data packages. `Toolshed` is still required as the runtime module: it supplies the loader logic, fuel patches, animation binding, and bundled `SCAssetPacks`.

Do not install this FUSE test pack at the same time as the RailLoader test pack unless you intentionally want duplicate test scenery and duplicate test operations. The production pattern is the same for both: the authoring system places scenery and operations, and Toolshed adds the loader behavior.
