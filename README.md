# test

a POC for making beat saber quest mods in C#

## Project layout

- `CoreMod`: mod-facing declarations grouped by feature
- `CoreMod/Patches`: native patch attributes and patch behavior kinds
- `CoreMod/Metadata`: mod identity declaration
- `CoreMod/Config`: config declaration
- `CoreMod/CustomTypes`: custom IL2CPP type declaration
- `CoreMod/BSML`: BSML component types, menu registration attributes, and the BSML Lite facade
- `CoreMod/BSML/Lite`: BSML Lite helper methods grouped by UI feature, plus Unity object wrappers
- `TypeGenerator`: generated game type declarations and metadata
- `Transpiler`: the top-level assembly and generation workflow
- `Transpiler/Discovery`: assembly, config, patch, custom type, BSML, and helper discovery
- `Transpiler/Generation/CoreMod/Config`: native config storage and initialization
- `Transpiler/Generation/CoreMod/BSML`: native menu and setup-tab registration
- `Transpiler/Generation/Patches`: native patch planning and hook emission
- `Transpiler/Generation/CustomTypes`: custom IL2CPP type emission
- `Transpiler/Generation/Mod`: mod source, helper methods, and generated-source formatting
- `Transpiler/Generation/Artifacts`: generated file output
- `Transpiler/Generation/Plans`: intermediate emission records shared by generators
- `Transpiler/IlMethodTranslator.*`: IL translation split between control flow, expressions, opcode emission, storage, calls, runtime mappings, and shared helpers
- `quest-mod`: QPM native project, CMake build, and qmod packaging
- `Tests`: sample and regression mod assemblies

# Ported mods

- PauseKey - https://github.com/dylanhook/PauseKey

## Credits (Quest C++ modding)

- [zoller27osu](https://github.com/zoller27osu), [Sc2ad](https://github.com/Sc2ad) and [jakibaki](https://github.com/jakibaki) - [beatsaber-hook](https://github.com/sc2ad/beatsaber-hook)
- [raftario](https://github.com/raftario)
- [Lauriethefish](https://github.com/Lauriethefish), [danrouse](https://github.com/danrouse) and [Bobby Shmurner](https://github.com/BobbyShmurner) for [this template](https://github.com/Lauriethefish/quest-mod-template)
