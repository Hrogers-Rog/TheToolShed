# Getting Started

## Performance And Periodic Stutters

Current Toolshed builds do not re-scan healthy service facilities or selective
interchanges every two seconds. Only unresolved definitions retry; those retries
share one lazy scene index and back off when no progress is possible. Scene
changes reset discovery immediately. Coupler and facility animation helpers
also avoid unchanged renderer, GameObject, and transform writes.

If a regular hitch remains, enable FUSE frame-spike diagnostics and attach both
`FUSE.log` and `Player.log`. The timestamp and named FUSE pump phase distinguish
Toolshed discovery work from game or third-party work.

## Requirements

- Railroader `2025.1.x`
- Unity Mod Manager
- [FUSE](https://github.com/F-U-S-E-E/FuseDevelopmentGroup) — Toolshed requires it and loads after it

## Install

1. Install Unity Mod Manager for Railroader.
2. Install FUSE and confirm it loads.
3. Place the `Toolshed` folder in `Railroader/Mods`.
4. Start Railroader and load a map.

Toolshed declares its own asset packs (`SCAssetPacks`) in its manifest, so FUSE
mounts the models automatically. There is nothing separate to install.

## Verify

Open the in-game console and run:

```
/fuse.loaded
```

Both `FUSE` and `Toolshed` should be listed and applied. If Toolshed is missing,
it was never discovered — check that the folder is in `Railroader/Mods` and
enabled in Unity Mod Manager. If it is listed as faulted, check `FUSE.log` for
the phase that failed.

Because Toolshed's content is delivered through FUSE, `/fuse.assets` will also
show its asset pack folders once they are mounted.

## What You Get

Toolshed does not change anything on its own — it adds capabilities that route
packages and locomotives use.

- **Service facilities** appear where a route package places them. On a route
  without any, you will not see coal towers or water columns simply from
  installing Toolshed. See [Service Facilities](SERVICE_FACILITIES.md).
- **Oil and wood firing** applies to locomotives whose definitions declare those
  fuels. See [Oil & Wood Firing](OIL_WOOD_FIRING.md).
- **Selective interchanges** are configured by route packages. See
  [Selective Interchanges](SELECTIVE_INTERCHANGES.md).
- **Link and pin couplers** apply to equipment that uses them, and are tunable in
  the Unity Mod Manager settings panel. See [Link & Pin](LINK_AND_PIN.md).
- **Hand turntables** are placed by route packages.

If you installed Toolshed expecting visible changes on a stock map, this is why
nothing looks different: it is a toolkit for route content, not a standalone
content pack.

## Settings

Toolshed's settings are in the Unity Mod Manager panel. The link-and-pin coupler
spacing values live there:

| Setting | Default |
| --- | --- |
| Equipment separation | `0.84` |
| Caboose separation | `0.84` |
| Locomotive/tender separation | `0.93` |
| Slack | `0.02` |

See [Link & Pin Couplers](LINK_AND_PIN.md) for what these do.

## Where The Files Are

| What | Where |
| --- | --- |
| Log | `%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE.log` |
| Unity log | `%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\Player.log` |
| Mod folder | `Railroader\Mods\Toolshed` |

## Reporting Problems

Include `FUSE.log`, `Player.log`, the output of `/fuse.loaded`, your mod list, and
the route package involved. Toolshed problems are usually route-content problems,
so naming the route matters.

Issues: <https://github.com/Hrogers-Rog/TheToolShed/issues>
