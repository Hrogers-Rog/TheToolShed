# Toolshed Service Facility Test (FUSE)

FUSE service-facility test package for Toolshed.

It places the Dillsboro bunker-c loader and wood shed with FUSE `world.scenery`, creates the storage/interchange operations, and uses `ToolshedServiceFacilities.json` for runtime service binding.

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

Production pattern: FUSE places scenery and operations, and Toolshed adds the loader behavior.
