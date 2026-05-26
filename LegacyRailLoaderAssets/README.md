# Toolshed Service Facility Assets

Temporary RailLoader/Strange Customs asset-pack wrapper for Toolshed service-facility scenery models.

Use this while RailLoader is still needed for in-game placement and alignment. The same assets also live in Toolshed's `SCAssetPacks` for FUSE. The service logic stays in `Toolshed.dll` either way.

Install this as a separate legacy/RailLoader mod folder:

```text
Railroader/Mods/Toolshed/Info.json
Railroader/Mods/Toolshed/Toolshed.dll
Railroader/Mods/Toolshed Service Facility Assets/Definition.json
Railroader/Mods/Toolshed Service Facility Assets/SCAssetPacks/ALWLoaders/...
Railroader/Mods/Toolshed Service Facility Assets/SCAssetPacks/WoodShed/...
```

Toolshed does not hard-require FUSE. RailLoader can use this wrapper for placement today, and FUSE can use the Toolshed asset packs at release.

Pair placed scenery with `ToolshedServiceFacilities.json` to make it a working service loader. Do not add RailLoader `LoaderBuilder` entries for these custom service assets.

Available scenery model identifiers:

- `ALW_Loader_TankLoader`
- `wood-shed`
- `Wood Shed`
- `Wood Fuel Shed`

Compatibility alias:

- `wood_fuel_shedp` maps to `wood-shed.prefab`

Animation map notes:

- `ALW_Loader_TankLoader`: map key `RotatePipe`, Unity clip `RotatePipeAction`
- `wood-shed`: map key `Wood Pile`
