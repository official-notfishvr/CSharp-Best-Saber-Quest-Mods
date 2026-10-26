# Beat Saber Mod Bridge

```text
PC mod source (real PC types and libraries)
             │ build
             ▼
        managed DLL ── PC reference catalog
             │ transpile + reviewed PC→Quest mappings
             ▼
Quest C++ / QPM project ── Quest SDK compile/package
```

`PcMod/HighlightBombs` recreates the two `BaseNoteVisuals` patches and includes the QuickOutline component and asset from the upstream mod's pinned submodule. The patch IL lowers, but the local Quest headers do not define QuickOutline's custom `Outline` component. Generation now rejects that missing Quest type explicitly instead of writing C++ with undeclared types. A Quest-native Outline component or an explicit mapping to a compatible Quest implementation is needed to finish this port.
