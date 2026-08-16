# Toolshed

A FUSE companion mod for Railroader that adds operational detail the base game
doesn't model: working service facilities, alternative locomotive fuels, filtered
interchanges, period couplers, and hand-operated turntables.

Toolshed requires [FUSE](https://github.com/F-U-S-E-E/FuseDevelopmentGroup) and
loads after it.

## Features

| Feature | What it adds |
| --- | --- |
| **Service Facilities** | Working coal towers, water columns, diesel and bunker-C stands, and wood sheds that actually load fuel, with animation and particle effects |
| **Oil & Wood Firing** | Oil- and wood-fired steam locomotives, with their own consumption model and fuel alerters |
| **Selective Interchanges** | Interchanges that accept only the car types you specify, instead of everything |
| **Link & Pin Couplers** | Period link-and-pin coupler visuals with configurable slack and spacing |
| **Hand Turntables** | Manually operated turntables |

## Install

1. Install Unity Mod Manager for Railroader.
2. Install FUSE.
3. Place the `Toolshed` folder in `Railroader/Mods`.
4. Start the game.

Toolshed ships its own asset packs (`SCAssetPacks`), which FUSE mounts
automatically — there is nothing extra to install for the models.

## Documentation

Full documentation is in **[docs/](docs/README.md)**.

- [Getting Started](docs/GETTING_STARTED.md) — install and verify
- [Service Facilities](docs/SERVICE_FACILITIES.md) — building working fuel and water stops
- [Selective Interchanges](docs/SELECTIVE_INTERCHANGES.md) — filtering interchange traffic
- [Oil & Wood Firing](docs/OIL_WOOD_FIRING.md) — alternative steam fuels
- [Link & Pin Couplers](docs/LINK_AND_PIN.md) — period couplers and slack settings

Authoring references:

- [`Examples/service-facility-setup-guide.md`](Examples/service-facility-setup-guide.md) — the full setup flow
- [`Examples/service-loader-component-blueprint.md`](Examples/service-loader-component-blueprint.md) — every editor setting in component order, with recommended values per fuel type

## Requirements

- Railroader `2025.1.x`
- Unity Mod Manager
- FUSE

## License

See [LICENSE](LICENSE).
