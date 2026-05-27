# Toolshed Selective Interchanges

RailLoader-era standalone package for Toolshed's selective interchange component.

This package intentionally includes only:

- `Toolshed.SelectiveInterchanges.SelectiveInterchangeComponent`
- JSON scanning for `ToolshedSelectiveInterchanges.json`
- selective car model ordering patches
- generic interchange exclusion support

It intentionally does not include:

- oil/wood firing
- service facility loaders
- custom scenery asset packs
- FUSE package metadata or dependency declarations

Do not install this alongside the full `Toolshed` mod in the same RailLoader install. Both provide the same `Toolshed.SelectiveInterchanges` runtime types, so choose one:

- RailLoader-only testing: use `Toolshed Selective Interchanges`
- FUSE/full Toolshed testing: use `Toolshed`

Put selective interchange definitions in either:

```text
Railroader/Mods/Your Route/ToolshedSelectiveInterchanges.json
Railroader/Mods/Your Route/SelectiveInterchanges/*.json
```

See `selective-interchange-readme.md` and the example JSON for the full data format.
