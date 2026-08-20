# Selective Interchanges

A vanilla Railroader interchange is generic — cars appear and disappear. A Toolshed
selective interchange turns that same track into a specific off-map industry or
connecting railroad, with traffic that makes sense for what it represents.

The worked example is the Ocona-Lufty RR at Smokemont behaving like an off-map
sawmill: empty flats and boxcars are ordered in, occasional loaded
building-supplies cars are delivered to the mill, the cars go off map, and loaded
lumber and poles return at the same interchange to be waybilled onward.

## For Players

You do not configure these — route packages do. What you notice is that an
interchange starts asking for particular cars and returning particular loads,
instead of accepting anything.

If an interchange is behaving oddly, the route package that configured it is where
the problem lives. Include the route name in any report.

## Authoring

### Config Location

Put a config file in either place inside your mod folder:

- `ToolshedSelectiveInterchanges.json` at the mod folder root
- any `.json` file inside a `SelectiveInterchanges` folder

Toolshed scans installed mod folders and applies matching definitions **after**
the game has created the industries.

### The Traffic Model

A selective interchange has four independent traffic types. They compose — use
only the ones your scenario needs.

| Field | Purpose |
| --- | --- |
| `emptyCarRequests` | Ask for empty cars to send to the off-map operation |
| `inboundLoads` | Accept loaded supplies the off-map operation consumes |
| `purchaseLoads` | Create vanilla buy/import loaders for loads the railroad can purchase |
| `outboundRoutes` | Create loaded-car orders at the interchange for off-map production returning |

Two details worth getting right:

**`emptyCarRequests` needs an origin.** For interchange-to-interchange traffic,
set `originWeights` or another origin field so the empties spawn at the active
external interchange rather than at the off-map interchange itself. Without it the
cars appear where they are going, which is not what you want.

**`inboundLoads` only acts when served.** By default Toolshed handles those cars
only when the interchange is actually being served, so cars sitting on the
interchange track are not removed by the normal industry tick.

**`purchaseLoads` is for buyable goods only.** Use it for what the railroad can
buy from the connecting railroad — Ocona-Lufty logs for Ravensford, in the worked
example. Do not put normal mill production here unless it genuinely should be
purchasable; production that returns loaded belongs in `outboundRoutes`.

### Reference

- [`../Examples/selective-interchange-readme.md`](../Examples/selective-interchange-readme.md)
  — the full field reference and a minimal working example
- [`../Examples/KingG.Appalachian-Railway.ToolshedSelectiveInterchanges.json`](../Examples/KingG.Appalachian-Railway.ToolshedSelectiveInterchanges.json)
  — a complete real configuration

There is also a legacy RailLoader build of this feature in
`RailLoaderSelectiveInterchanges/` for pre-FUSE installs.

## Related

- [Getting Started](GETTING_STARTED.md)
- [Service Facilities](SERVICE_FACILITIES.md)
