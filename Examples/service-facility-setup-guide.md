# Universal Service Facility Setup Guide

This guide is for FUSE scenery asset authors who want one Toolshed dependency for service loaders instead of separate oil, coal, wood, diesel, or water loader mods.

The short version: place a normal scenery asset with FUSE, define the industry/track span in FUSE operations data, then bind Toolshed service behavior with `ToolshedServiceFacilities.json`. Finished Unity prefabs may also carry `Toolshed.ServiceFacilities.UniversalServiceFacilityComponent` directly, but the JSON binding path is the preferred route because it keeps service behavior out of the asset pack.

## Authoring Paths

| Path | Use when | What places the model | What adds service behavior |
| --- | --- | --- | --- |
| FUSE package | You are shipping or testing the FUSE route package | FUSE `world.scenery` | Toolshed runtime reads `ToolshedServiceFacilities.json` |
| Unity prefab components | You are finalizing a prefab and want everything self-contained | FUSE/Railroader prefab instancing | Components already on the prefab |

The important rule is that only Toolshed owns the service logic. FUSE should provide placement, spans, industries, and storage definitions.

## Supported Loads

| Load ID | Units | Typical use |
| --- | --- | --- |
| `coal` | Pounds | Coal towers, docks, chutes |
| `water` | Gallons | Water columns, wells, towers |
| `diesel-fuel` | Gallons | Diesel fuel stands |
| `bunker-c` | Gallons | Oil-firing stands, tank loaders |
| `pulpwood` | Pounds | Wood sheds and wood loaders |

Wood note: Toolshed uses the base-game `pulpwood` load as wood fuel. Loaders can transfer it, and steam equipment with a `pulpwood` fuel slot can consume it. The current rate is a first-pass estimate: `1.8` pulpwood pounds per coal pound.

## Runtime Or Prefab Components

| Component | Required | Purpose |
| --- | --- | --- |
| `UniversalServiceFacilityComponent` | Yes | Configures one service load |
| `CarLoadTargetLoader` | Yes | Vanilla physical transfer component |
| `KeyValueObject` | Yes | Stores request/loading booleans |
| `CarLoaderSequencer` | Recommended | Drives vanilla prepare/load animation flow |
| `GlobalKeyValueObject` | Recommended | Stable save/multiplayer control ID |
| `Industry` | Finite supply only | Owns persisted storage |
| `IndustryUnloader` | Optional | Lets delivery cars unload into storage |
| `InterchangedIndustryLoader` | Optional | Lets cars buy load off map |
| `Interchange` | Interchange only | Required by vanilla interchange purchase |
| `TrackSpan` | Industry ops only | Defines where delivery/interchange cars stand |

When using `ToolshedServiceFacilities.json`, Toolshed creates the loader-side `KeyValueObject`, `GlobalKeyValueObject`, `CarLoadTargetLoader`, `CarLoaderSequencer`, click toggle, and `UniversalServiceFacilityComponent` at runtime. Industry-side components still belong in FUSE operations data because they define storage, delivery, and interchange behavior.

Toolshed also attaches a `ServiceFacilityPickable` to the visible scenery root. That matters because Railroader's object picker starts from the mesh collider that the mouse hit and searches upward for an `IPickable`; putting the pickable only on a hidden loader child will not work for normal asset meshes.

The car, tender, or locomotive being loaded also needs:

- A `LoadSlot` with `RequiredLoadIdentifier` matching the facility `serviceLoadId`.
- A `CarLoadTarget` whose `slotIndex` points at that slot.
- Enough target radius to overlap the loader radius when stopped under the loader.

## Key Flow

Use these default keys unless the prefab already has a different key scheme:

| Key | Written by | Read by | Purpose |
| --- | --- | --- | --- |
| `request` | Player toggle/control | `CarLoaderSequencer` | Player wants loading |
| `prepareLoad` | `CarLoaderSequencer` | Animator/effects | Move hose/chute/spout into position |
| `canLoad` | `CarLoaderSequencer` | `CarLoadTargetLoader` | Loader may search and transfer |
| `isLoading` | `CarLoadTargetLoader` | `CarLoaderSequencer` | Actual transfer is happening |
| `animateLoad` | `CarLoaderSequencer` | Animator/effects | Show flow/loading effect |

## UniversalServiceFacilityComponent Fields

| Field | Meaning |
| --- | --- |
| `serviceLoadId` | `coal`, `water`, `diesel-fuel`, `bunker-c`, or `pulpwood` |
| `infiniteSupply` | True means vanilla infinite loading; false drains linked industry storage |
| `facilityCapacity` | Storage capacity in the load's native units |
| `currentStorage` | Debug/seed mirror for linked industry storage |
| `loadingRate` | Units per real second transferred |
| `serviceRadius` | Loader-side radius used with car-side target radius |
| `enableExtendedTenderSearch` | Searches nearby cars for a matching tender/service `CarLoadTarget` when vanilla point loading sees the locomotive body or a supply car first |
| `extendedSearchRadius` | Nearby car discovery radius for the extended service search |
| `extendedLoadTargetRadius` | Max flat distance from the service point to the matching `CarLoadTarget` |
| `maximumSpeedMph` | Max car speed allowed for loading |
| `serviceTrackSpan` | Track span for industry delivery/interchange |
| `linkedIndustry` | Industry storage used for finite loading |
| `requirePlayerOwnedCars` | Matches vanilla service behavior by default |
| `canPurchaseThroughInterchange` | Enables vanilla off-map purchase when configured |
| `purchaseDelayDays` | Future/documentation value; vanilla currently uses about one day |
| `debugLogging` | Enables `[ServiceFacility]` and `[ServiceFacility][Loader]` logs |

`loadingRate` is the same unit used by the target slot: gallons per real second for liquids and pounds per real second for solid fuels. Vanilla `CarLoadTargetLoader` transfers once per second, so a `10,000` gallon bunker-C tender at `6` gal/s takes about 27 minutes 45 seconds from empty.

## Common Configurations

### Infinite Water

- `serviceLoadId = "water"`
- `infiniteSupply = true`
- `loadingRate = 60`
- `serviceRadius = 0.2`
- `maximumSpeedMph = 5`
- `canPurchaseThroughInterchange = false`

### Finite Bunker-C

- `serviceLoadId = "bunker-c"`
- `infiniteSupply = false`
- `facilityCapacity = 8000`
- `loadingRate = 6`
- `linkedIndustry = your service industry`
- `serviceTrackSpan = your delivery track span`
- `enableExtendedTenderSearch = true` for custom scenery where the service point may see the locomotive body or a nearby supply car before it sees the tender
- `configureReceivingUnloader = true`
- `configureInterchangeLoader = true` only if an `Interchange` exists
- `canPurchaseThroughInterchange = true` only if you want off-map purchase

### Diesel Fuel

- `serviceLoadId = "diesel-fuel"`
- `infiniteSupply = false`
- `facilityCapacity = 8000`
- `loadingRate = 60`
- `configureReceivingUnloader = true`
- `configureInterchangeLoader = optional`

Vanilla diesel locomotives consume slot 0 as `diesel-fuel`.

### Coal

- `serviceLoadId = "coal"`
- `infiniteSupply = false`
- `facilityCapacity = 100000`
- `loadingRate = 2000` to `5000`
- `configureReceivingUnloader = true`
- `configureInterchangeLoader = optional`

### Wood

- `serviceLoadId = "pulpwood"`
- `infiniteSupply = true` for a simple shed, or false for delivered wood storage
- `facilityCapacity = 100000` if finite
- `loadingRate = 250` to `500`
- `enableExtendedTenderSearch = true` for open sheds or loaders placed beside existing service tracks
- `configureReceivingUnloader = true` if wood should arrive by car

Wood-fired steam equipment must have a `pulpwood` load slot and a `water` load slot. Current consumption is intentionally simple and should be tuned after live testing.

## Shared Runtime Binding

`ToolshedServiceFacilities.json` is the FUSE-to-Toolshed bridge. Put it at the root of the FUSE package folder, in `ServiceFacilities/*.json`, or inside an asset pack folder as an exact `ToolshedServiceFacilities.json` file. Toolshed scans installed mod folders and attaches service behavior after the scenery, loads, industries, and spans exist.

```json
{
  "facilities": [
    {
      "id": "example-bunker-c-loader",
      "targetObjectName": "example:scenery:bunker-c-loader",
      "targetObjectNames": [
        "example:scenery:bunker-c-loader"
      ],
      "modelIdentifier": "ALW_Loader_TankLoader",
      "serviceLoadId": "bunker-c",
      "sourceIndustryId": "example:industry:engine-service",
      "serviceTrackSpanId": "example:span:bunker-c-unload",
      "serviceTrackSpanIds": [
        "example:span:bunker-c-unload"
      ],
      "infiniteSupply": false,
      "facilityCapacity": 16000,
      "loadingRate": 6,
      "serviceRadius": 0.9,
      "enableExtendedTenderSearch": true,
      "extendedSearchRadius": 20,
      "extendedLoadTargetRadius": 10,
      "maximumSpeedMph": 5,
      "requirePlayerOwnedCars": true,
      "loaderAtTrackSpanCenter": true,
      "interactionRadius": 8,
      "requestTitle": "Bunker-C Loader",
      "requireServiceCondition": true,
      "serviceConditionBoolKey": "request",
      "serviceConditionExpectedValue": true,
      "animations": [
        {
          "animationMapKey": "PipeTurner",
          "boolKey": "prepareLoad",
          "speed": 1,
          "fallbackTransformNames": [
            "PipeTurner",
            "RotatePipe"
          ],
          "fallbackTransformOverrides": [
            {
              "transformName": "RotatePipe",
              "inactiveLocalEuler": { "x": 0, "y": 80, "z": 0 },
              "activeLocalEuler": { "x": 0, "y": 0, "z": 0 }
            }
          ],
          "fallbackDurationSeconds": 1.25
        }
      ],
      "particleEffects": [
        {
          "boolKey": "animateLoad",
          "requiredBoolKey": "request",
          "requiredBoolExpectedValue": true,
          "createIfMissing": true,
          "requireParentTransform": true,
          "parentTransformName": "FuelPoint",
          "parentTransformNames": [
            "FuelPoint",
            "Toolshed_FlowOrigin",
            "OilFlowOrigin",
            "FlowOrigin",
            "PipeTip",
            "SpoutTip"
          ],
          "localPosition": {
            "x": 0,
            "y": 0,
            "z": 0
          },
          "localEuler": {
            "x": 90,
            "y": 0,
            "z": 0
          },
          "emissionRate": 90,
          "startLifetime": 0.55,
          "startSpeed": 0.85,
          "startSize": 0.045,
          "gravityModifier": 0.8,
          "overrideStartColor": true,
          "startColor": {
            "r": 0.16,
            "g": 0.09,
            "b": 0.035,
            "a": 0.9
          },
          "createVisibleStream": true,
          "streamUsesWorldDown": true,
          "streamLocalStart": {
            "x": 0,
            "y": 0,
            "z": 0
          },
          "streamLength": 0.62,
          "streamWidth": 0.04,
          "streamColor": {
            "r": 0.16,
            "g": 0.09,
            "b": 0.035,
            "a": 0.9
          },
          "debugOriginMarker": true
        }
      ]
    }
  ]
}
```

Use `sourceIndustryId` when the loader should be finite and drain persisted industry storage. Leave it blank or set `infiniteSupply = true` for infinite water-style loaders.

`targetObjectName` should match the FUSE `world.scenery` id. Use `targetObjectNames`, `sourceIndustryIds`, and `serviceTrackSpanIds` only when you need aliases during a rename or migration. `modelIdentifier` and `modelIdentifiers` are fallbacks for simple packs, but stable object ids are better when a route has more than one copy of the same model.

`animations` follows boolean loader flow. Use it for pipe, chute, hose, or gate movement:

- `prepareLoad`: move into loading position.
- `animateLoad`: show active loading flow.

Use `fallbackDurationSeconds` when `useTransformFallback` is enabled and the fallback transform should move at a fixed, scenery-friendly speed. If it is omitted, Toolshed uses the animation clip length when available and remembers the last known length during scenery reloads; the built-in safety default is one second.

Use `fallbackTransformNames` when the same asset exists in multiple community exports with different animated-empty names. Toolshed tries `fallbackTransformName` first, then each entry in `fallbackTransformNames`.

Use `fallbackTransformOverrides` when a fallback transform has a different local axis or rest pose than the primary export. This is useful for older community prefabs where `RotatePipe` and `PipeTurner` represent the same visual pipe but do not share the same local rotation basis.

Use `requireServiceCondition` when a chute, pipe, spout, or hose must be in the service position before transfer. For the ALW bunker-C loader, `request = true` means the pipe is down, so `serviceConditionBoolKey = "request"` blocks both vanilla loading and Toolshed's extended tender search while the pipe is raised.

`particleEffects` can either target existing particle systems by name or create a small runtime particle system. For oil loaders, prefer `boolKey = "animateLoad"` so the effect follows the vanilla loader-visible flow state. Add `requiredBoolKey = "request"` when the effect must also require the pipe-down state. If an asset already has a named particle object, set `effectObjectName` or `effectObjectNames` and leave `createIfMissing = false`. If the asset has no effect yet, set `createIfMissing = true`, choose `parentTransformName` near the pipe/nozzle, then tune `localPosition`, `localEuler`, size, lifetime, speed, and color in small steps.

`parentTransformNames` is an optional fallback list for community assets where the visible outlet empty may have different names between exports. The first matching transform is used. For precise oil/water flow, prefer a normal Unity empty at the spout tip. `Toolshed_FlowOrigin` is the generic Toolshed convention; asset-specific names are also fine when the JSON lists them. The ALW tank loader uses `FuelPoint`. Dual-track chute exports use `Chute.CoalStart` and `Chute.CoalStart.001`. The tower loader uses `SingleLoadChute`.

`flowOriginFollowTransformName` is available for editor-placed origins that need to ride along with an animated pipe/chute, but use it carefully. If the exported transform reports nonsense world coordinates after FUSE loads the model, the effect will jump off-map. For the ALW tank loader test asset, the stable setup is a root-local outlet offset and no follow transform.

Best practice is to place a normal Unity empty at the exact pipe, chute, hose, or spout tip:

- In the Unity prefab, add an empty GameObject at the outlet. Parent it to the moving pipe/chute part so it follows the animation.
- Use a stable name and list that name in `parentTransformName` or `parentTransformNames`. Recommended generic name: `Toolshed_FlowOrigin`.
- Known shipped names: `FuelPoint` for the ALW tank loader, `Chute.CoalStart` and `Chute.CoalStart.001` for a dual-track chute, and `SingleLoadChute` for a tower loader.

Put the marker origin exactly where the fluid/coal/wood should leave the asset; Toolshed can then use zero local offsets and avoid guessing from a pivot that may be off-model. Set `requireParentTransform = true` for these effects when a bad fallback position would look worse than no effect.

Avoid shipping custom `ToolshedFlowOrigin` component entries in FUSE-mounted asset-pack `Definitions.json` files. FUSE direct asset loading strips unsupported component kinds during sanitation, so the reliable public setup is a normal prefab empty plus JSON binding.

Temporary fallback for assets that do not ship with a Unity empty:

1. Set `requireParentTransform = false`.
2. Set `localPosition` to the outlet's local offset from the placed scenery root.
3. Keep `requiredBoolKey = "request"` or another service-position key so the effect only plays while the spout/chute is in the loading position.
4. Treat this as a stopgap. A real prefab empty parented to the moving part is the release-quality setup.

For dark liquid effects, `createVisibleStream = true` also creates a thin `LineRenderer` stream alongside the particles, which is much easier to see in shadow. The stream follows the same `localPosition` and `localEuler` as the particle emitter. Use `streamLength` to control how far the stream falls, `streamWidth` for thickness, and `streamColor` for color. Set `debugOriginMarker = true` only while tuning offsets; it shows a small yellow sphere at the effect origin.

`storageAnimations` follows storage percentage instead of a boolean. Use it for visible inventory such as a wood pile:

- 0% storage samples the start of the clip.
- 50% storage samples the middle of the clip.
- 100% storage samples the end of the clip.

```json
{
  "storageAnimations": [
    {
      "animationMapKey": "Wood Pile",
      "loadId": "pulpwood",
      "capacity": 180000
    }
  ]
}
```

## FUSE Pattern

FUSE should place the same custom model with `world.scenery`, then use the same `ToolshedServiceFacilities.json` binding.

```json
{
  "schemaVersion": "1.0",
  "id": "Example.ServiceFacilities",
  "name": "Example Service Facilities",
  "author": "Example",
  "modVersion": "1.0.0",
  "coordinateSpace": "world",
  "operations": {
    "industries": {
      "example:industry:engine-service": {
        "name": "Example Engine Service",
        "position": { "x": 0, "y": 0, "z": 0 },
        "components": {}
      }
    }
  },
  "world": {
    "scenery": {
      "example:scenery:bunker-c-loader": {
        "assetIdentifier": "scenery://ALW_Loader_TankLoader",
        "position": { "x": 0, "y": 0, "z": 0 },
        "rotation": { "x": 0, "y": 0, "z": 0 },
        "scale": { "x": 1, "y": 1, "z": 1 }
      }
    }
  }
}
```

Use FUSE `operations.loaders` only for cloning existing vanilla-style loader prefabs that already contain the right loader components. For custom Toolshed service assets, use `world.scenery` plus `ToolshedServiceFacilities.json`.

## Finite Storage Setup

1. Add a stable `Industry.identifier`.
2. Add a `TrackSpan` where delivery cars stand.
3. Assign `linkedIndustry`.
4. Set `infiniteSupply = false`.
5. Add or configure `IndustryUnloader`.
6. Set `sharedStorage = true`.
7. Use `Seed Linked Industry Storage` from the component context menu for starting inventory.

For a service facility, the receiving `IndustryUnloader` is storage only. Set its storage consumption rate to `0` and do not add a formulaic industry component just to represent wood, coal, diesel, or bunker-C use. Toolshed hides Railroader's misleading zero-rate `Consumes` panel row for this storage-only case.

## Interchange Purchase Setup

Use interchange for `coal`, `diesel-fuel`, `bunker-c`, and `pulpwood` for wood fuel.

Do not use interchange for `water`.

Requirements:

- Linked `Industry`.
- Child `Interchange`.
- `InterchangedIndustryLoader`.
- Assigned `serviceTrackSpan`.
- Sensible `load.costPerUnit` if purchase cost should matter.

Vanilla does not use one shared off-map fuel source. Each active interchange owns its own child
`InterchangedIndustryLoader` entries. If a load should be buyable from Sylva, Whittier, and Andrews,
add the same load-specific child loader under each interchange industry with that interchange's own
track spans.

For reference in the Dillsboro/Sylva/Andrews test graph:

- East Whittier interchange: `ewh-interchange`, spans `Pyih`, `Pcll`.
- Sylva interchange: `sylva-interchange`, spans `Pfaj`, `Pin7`, `P7ec`.
- Andrews interchange: `andrews-interchange`, spans `Puzp`, `P0ih`, `P0vg`, `Phcl`.

`pulpwood` is non-importable in vanilla and has `costPerUnit = 0`, so an interchange loader can fill
cars for free unless the test pack overrides the load with a fuel-purchase cost. Keep
`importable = false` so normal pulpwood industries still behave like captive/local traffic, but set
`costPerUnit` for the service-fuel test if buying wood fuel should charge the railroad.

Vanilla fills cars off map and returns them after roughly one game day.

## Asset Notes

Toolshed includes the loader component, fuel patches, and `SCAssetPacks` for public release.

The ALW tank loader spout animation is in the prefab animation map as `PipeTurner`. Older test bundles used `RotatePipe`, so keep the map key in `ToolshedServiceFacilities.json` matched to the actual prefab bundle you are shipping.

The wood shed prefab includes a `Wood Pile` animation-map entry. Use `wood-shed` for new scenery and keep `wood_fuel_shedp` only as a compatibility alias.

FUSE mounts the `SCAssetPacks` bundled in Toolshed and places the scenery ids used by the service binding.

## Troubleshooting

The loader never starts:

- Confirm Toolshed is enabled.
- Confirm `serviceLoadId` resolves.
- Confirm the control writes `request = true`.
- Confirm the sequencer writes the same `canLoad` key the loader reads.
- Confirm the game is host; vanilla loader logic is host-only.

The loader animates but does not fill:

- Confirm the car is below `maximumSpeedMph`.
- Confirm the car is player-owned if `requirePlayerOwnedCars` is true.
- Confirm the car has `CarLoadTarget`.
- Confirm the target `slotIndex` points at the matching load slot.
- Confirm `RequiredLoadIdentifier` exactly matches `serviceLoadId`.
- Confirm loader and target radii overlap.
- For custom scenery beside a longer locomotive/tender, set `enableExtendedTenderSearch = true` and tune `extendedSearchRadius`/`extendedLoadTargetRadius`.

Finite supply stops immediately:

- Confirm `linkedIndustry` is assigned.
- Confirm storage contains the same load ID.
- Confirm `IndustryUnloader` and loader both use shared storage.

Interchange does not buy:

- Confirm an `Interchange` exists under the linked industry.
- Confirm an `InterchangedIndustryLoader` exists.
- Confirm the load is not `water`.
- Confirm the railroad can afford the purchase.

## Release Checklist

- [ ] Choose one setup path per asset: runtime JSON binding or explicit prefab components.
- [ ] Use stable `GlobalKeyValueObject.globalObjectId` values.
- [ ] Keep `debugLogging` off for release unless intentional.
- [ ] Validate asset identifiers with `/fuse.assets` or FUSE health tooling.
- [ ] Test save/reload persistence.
- [ ] Test delivery, service loading, and interchange separately.
