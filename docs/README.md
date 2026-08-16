# Toolshed Documentation

Toolshed is a FUSE companion mod that adds operational detail to Railroader:
working service facilities, alternative locomotive fuels, filtered interchanges,
period couplers, and hand turntables.

## Players

| Doc | What it covers |
| --- | --- |
| [Getting Started](GETTING_STARTED.md) | Install, verify, and troubleshoot |
| [Service Facilities](SERVICE_FACILITIES.md) | Using working coal, water, diesel, bunker-C, and wood stops |
| [Oil & Wood Firing](OIL_WOOD_FIRING.md) | Running oil- and wood-fired steam |
| [Link & Pin Couplers](LINK_AND_PIN.md) | Period couplers and slack settings |
| [Selective Interchanges](SELECTIVE_INTERCHANGES.md) | Interchanges that accept only certain cars |

## Package Authors

Placing Toolshed content in your own route package:

- [Service Facilities](SERVICE_FACILITIES.md#authoring) — component setup
- [`../Examples/service-facility-setup-guide.md`](../Examples/service-facility-setup-guide.md) — the full authoring flow
- [`../Examples/service-loader-component-blueprint.md`](../Examples/service-loader-component-blueprint.md) — every setting in component order, with recommended values
- [Selective Interchanges](SELECTIVE_INTERCHANGES.md#authoring) — filter configuration
- [`../Examples/selective-interchange-readme.md`](../Examples/selective-interchange-readme.md) — worked example
- [`../Examples/link-pin-coupler-readme.md`](../Examples/link-pin-coupler-readme.md) — coupler model reference

## Offline Manual

- [Toolshed User Manual](pdf/Toolshed-User-Manual.pdf) — every feature in one printable document

Rebuild with `python scripts/build_pdfs.py` (needs `pip install reportlab`).

## Quick Answers

**Nothing loads.** Toolshed requires FUSE and loads after it. Run `/fuse.loaded`
and confirm both are listed and applied.

**A service facility does nothing when I stop at it.** The car has to be spotted
on the load point's span, and the facility needs storage with fuel in it. See
[Service Facilities](SERVICE_FACILITIES.md#troubleshooting).

**My locomotive won't take oil or wood.** The locomotive definition has to declare
that fuel. See [Oil & Wood Firing](OIL_WOOD_FIRING.md).

**Where are the logs?**
`%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE.log`

## Project

- **Repository:** <https://github.com/Hrogers-Rog/TheToolShed>
- **Requires:** [FUSE](https://github.com/F-U-S-E-E/FuseDevelopmentGroup)
