# Toolshed for RailLoader

This is the RailLoader-compatible Toolshed package. It includes link-and-pin
couplers, oil/wood firing, selective interchanges, and service-facility support
without requiring FUSE.

Version 0.3.1 carries the Toolshed stutter fix: configuration roots are scanned
once when the mod initializes. If no service-facility definitions exist, the
mod no longer walks the complete `Mods` tree every two seconds.

## Installation

Extract the `Toolshed` folder into Railroader's `Mods` directory. Remove older
RailLoader Toolshed folders and the obsolete standalone
`Toolshed Selective Interchanges` folder before installing this package.
