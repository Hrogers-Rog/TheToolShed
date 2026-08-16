# Oil & Wood Firing

Steam locomotives in Railroader burn coal. Toolshed adds oil (bunker C) and wood
firing, each with its own consumption model, fuel gauges, and alerter behavior.

## Fuels

| Fuel | Load id | Notes |
| --- | --- | --- |
| Bunker C | `bunker-c` | Heavy fuel oil, measured in gallons |
| Wood | `pulpwood` | Uses the existing pulpwood load, so it works with pulpwood industries and cars |
| Coal | `coal` | Base game, unchanged |

Wood deliberately reuses `pulpwood` rather than inventing a Toolshed-only load.
A wood-burner can be fueled from the same pulpwood your route already hauls.

## Conversion Rates

Toolshed converts between fuels so a locomotive's existing coal-based capacity and
consumption still mean something:

- **Bunker C**: `0.043` gallons per pound of coal displaced. Density is 62 lb per
  cubic foot, which works out to roughly 8.29 lb per gallon.
- **Wood**: `1.8` pounds of wood per pound of coal — wood carries less energy per
  pound, so a wood-burner goes through noticeably more of it.

That 1.8 ratio is the number to keep in mind when planning a wood-fired
operation: tender capacity that lasted a full run on coal will not.

Bunker C loads at 60 gallons per second at a service stand.

## Running One

From the cab, an oil- or wood-fired locomotive behaves like a coal burner — the
fireman's job is the same and the fuel gauge reads the same way. What changes is
where you refuel and how fast the supply drops.

The auto-engineer's fuel alerter accounts for the active fuel, so low-fuel
warnings fire against the real remaining supply rather than a coal-equivalent
figure.

## Fueling

Oil and wood locomotives refuel at [service facilities](SERVICE_FACILITIES.md)
that carry the matching load — a bunker-C stand or a wood shed. A coal tower will
not fuel them.

If a locomotive sits at a facility and nothing transfers, the usual cause is a
fuel mismatch: confirm the facility's load id matches the locomotive's fuel.

## Which Locomotives

Toolshed does not convert locomotives on its own. A locomotive burns oil or wood
only when its **definition declares that fuel**, which is the job of the equipment
mod or route package that ships it.

Installing Toolshed will not change any base-game locomotive. If you expected a
locomotive to burn oil and it still burns coal, that locomotive's definition does
not declare the fuel.

Toolshed resolves the fuel from the locomotive, and falls back to its fuel car
(tender) when the locomotive itself does not declare one — so a tender can carry
the fuel definition.

## Troubleshooting

**My locomotive still burns coal.** Its definition does not declare oil or wood.
That is an equipment-mod question, not a Toolshed one.

**It won't take fuel at a stand.** The stand's load has to match the locomotive's
fuel. Bunker C and diesel fuel are different loads.

**Wood runs out much faster than coal did.** Expected — see the 1.8:1 ratio above.

**The fuel gauge looks wrong.** Check `FUSE.log`. Toolshed logs fuel-resolution
problems once per piece of equipment rather than every frame, so search for the
locomotive's name.

## Related

- [Service Facilities](SERVICE_FACILITIES.md) — where these fuels come from
- [Getting Started](GETTING_STARTED.md)
