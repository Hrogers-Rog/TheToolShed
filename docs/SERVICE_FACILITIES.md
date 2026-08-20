# Service Facilities

Working fuel and water stops. A Toolshed service facility is a placed structure —
coal tower, water column, diesel or bunker-C stand, wood shed — that actually
transfers its load into a spotted locomotive or tender, with animation and
particle effects while it runs.

## Supported Loads

| Load | Id |
| --- | --- |
| Coal | `coal` |
| Water | `water` |
| Diesel fuel | `diesel-fuel` |
| Bunker C | `bunker-c` |
| Wood | `pulpwood` |

Wood uses the `pulpwood` load id rather than a Toolshed-specific one, so it
interoperates with existing pulpwood industries and cars.

## Using One

1. Spot the locomotive or tender so the fuel point sits on the facility's load
   point span.
2. The facility begins transferring on its own.
3. Watch the animation and particles — they run while the transfer is active and
   stop when it completes.

Two things must be true for anything to happen: the car has to actually be on the
load point's span, and the facility has to have fuel available. A facility backed
by finite storage stops when its storage is empty; an infinite one never does.

Liquid loads default to a transfer rate of 60 units per unit time.

## Storage

A facility is either **infinite** or **finite**.

Infinite facilities — typically water — always have supply. Finite ones draw down
a storage component that has to be refilled, usually by delivering the fuel in
cars. A coal tower that has run out needs a coal delivery before it will fuel
anything again.

Finite storage is what makes fuel logistics part of the operation rather than a
formality. It is also the most common reason a facility "stops working."

## Troubleshooting

**Nothing happens when I stop at the facility.**
Check the spot first. The fuel point on the car has to be within the load point's
span, which is narrower than the visible structure. Then check whether the
facility has storage remaining.

**It worked before and stopped.**
Finite storage is empty. Deliver more of that load.

**The animation runs but nothing transfers.**
The car may not accept that load — a coal tender will not take bunker C. Confirm
the load id matches what the equipment carries.

**The structure is missing entirely.**
That is an asset or route problem rather than a Toolshed one. Run `/fuse.assets`
to confirm the pack mounted, and `/fuse.loaded` to confirm the route package
applied.

**The tank and working loader appear as two separate objects.**
Do not place a second functional stand beside an already placed scenery model.
For an older asset, set `targetObjectName` to the placed scenery id and
`loadPointId` to the existing outlet transform (for example,
`FuelLoaderFill`). Toolshed binds the service component to that transform. Set
`serviceLoadId` explicitly when the source industry/span does not identify one
unambiguously.

## Authoring

Placing service facilities in your own route package is documented in depth in
the reference guides:

- [`../Examples/service-facility-setup-guide.md`](../Examples/service-facility-setup-guide.md)
  — the full setup flow: authoring paths, editor component setup, runtime and
  prefab components, the prefab contract, the FUSE pattern, finite storage,
  interchange purchase setup, and a release checklist.
- [`../Examples/service-loader-component-blueprint.md`](../Examples/service-loader-component-blueprint.md)
  — every `ToolshedServiceStorage` and `ToolshedServiceLoadPoint` setting in
  component order, with recommended values for coal, water, diesel, bunker-C, and
  wood.

The blueprint is the one to keep open while configuring: it gives concrete
recommended values per fuel type rather than describing the fields abstractly.

Common configurations covered there include infinite water, finite bunker-C,
diesel fuel, coal, and wood, plus how to repurpose an existing stand loader (for
example, a diesel stand into a bunker-C stand).

## Related

- [Getting Started](GETTING_STARTED.md)
- [Oil & Wood Firing](OIL_WOOD_FIRING.md) — the locomotives that consume these fuels
