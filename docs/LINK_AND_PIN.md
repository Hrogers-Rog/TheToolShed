# Link & Pin Couplers

Period link-and-pin coupler visuals for locomotives, tenders, and cars, with
configurable spacing and slack.

Toolshed's link-and-pin support is **definition-driven**. It changes an end of a
vehicle only when that vehicle has a `DetailModel` component using the
`LinkAndPinCoupler/link-pin-coupler` asset. It does not replace every coupler in
the game.

## What It Does

On a marked end, Toolshed:

- Hides the vanilla knuckle coupler on that end only
- Shows the link-and-pin asset as a loose link
- Hides the imported coupler pocket and pin from the asset, so the model can use
  its own pocket or drawbar
- Leaves only one loose link visible when two link-and-pin ends couple together
- Adds a small slack adjustment to marked ends
- Adds customization checkboxes for every marked end

## Player Customization

The equipment customization window exposes the parts per end. With one marked end:

- `Loose Link`
- `Pin`
- `Pocket`

With both ends marked, each is prefixed `A-End` or `B-End`.

The loose link defaults **on**, so existing setups keep working. The imported pin
and pocket default **off**, because many locomotive and tender models already
include their own pin or pocket detail — turning them on usually double-renders
the part. These choices save per car.

## Spacing And Slack

Four values in the Unity Mod Manager settings panel control how close link-and-pin
equipment couples:

| Setting | Default | Applies to |
| --- | --- | --- |
| Equipment separation | `0.84` | General equipment |
| Caboose separation | `0.84` | Cabooses |
| Locomotive/tender separation | `0.93` | Locomotive and tender pairs |
| Slack | `0.02` | Free play in the coupling |

Locomotive/tender spacing is wider by default because that pairing usually has a
drawbar rather than a link.

These are visual and slack-behavior values. Raise separation if your models
intersect when coupled; lower it if the gap looks too wide.

## Authoring

Full setup is in
[`../Examples/link-pin-coupler-readme.md`](../Examples/link-pin-coupler-readme.md).
The short version:

### End Naming

The component name tells Toolshed which end it belongs to:

- `Link And Pin Coupler F` — front / A end
- `Link And Pin Coupler R` — rear / B end

Names ending in `Front` or `Rear` also work.

### Definition Component

Paste an object from
[`../Examples/link-pin-coupler-detail-model.json`](../Examples/link-pin-coupler-detail-model.json)
into the vehicle's `components` array, then tune `parent.path`,
`transform.position`, `transform.rotation`, and `transform.scale`.

For a locomotive with a fixed front drawbar, put the visual link only on the
tender or rear pocket end. For freight cars, add one component per end that should
have link-and-pin visuals.

### Asset Ids Must Match

```json
"model": {
  "assetPackIdentifier": "LinkAndPinCoupler",
  "assetIdentifier": "link-pin-coupler"
}
```

If those ids change, Toolshed treats the component as an ordinary detail model and
will **not** hide the vanilla coupler for that end. This is the most common reason
a link-and-pin setup renders both couplers at once.

## Related

- [Getting Started](GETTING_STARTED.md) — where the settings panel lives
