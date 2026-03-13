# test

a POC for making beat saber quest mods in C#

what is working?

- `CoreMod` contains the mod-facing attributes
- `Transpiler` consumes prebuilt assemblies instead of invoking `dotnet build`
- `Transpiler` now works from compiled assemblies / IL instead of parsing C# syntax trees
- `Transpiler` consumes shared generated type metadata when it is available
- `Transpiler` works for `SampleMod`
- `TypeGenerator` can emit `GeneratedTypes.cs` plus `GeneratedTypes.metadata.json`

## Credits (Quest C++ modding)

- [zoller27osu](https://github.com/zoller27osu), [Sc2ad](https://github.com/Sc2ad) and [jakibaki](https://github.com/jakibaki) - [beatsaber-hook](https://github.com/sc2ad/beatsaber-hook)
- [raftario](https://github.com/raftario)
- [Lauriethefish](https://github.com/Lauriethefish), [danrouse](https://github.com/danrouse) and [Bobby Shmurner](https://github.com/BobbyShmurner) for [this template](https://github.com/Lauriethefish/quest-mod-template)
