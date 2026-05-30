# UdonStatic

Experimental UdonSharp compiler extension for `static` fields backed by one scene-wide global store.

Udon does not expose native C# static memory. This package emulates static field storage by creating exactly one `__UdonStaticGlobalStore` object in the scene and lowering static field access to typed Data arrays on that store.

## Installation

Install through VCC/VPM.

Required VPM dependency:

- `com.vrchat.worlds >= 3.9.0`

Repository release tags should match `package.json` version, for example `0.1.0` or `v0.1.0`.

## Usage

Source:

```csharp
using UdonSharp;
using UnityEngine;

public class Example : UdonSharpBehaviour
{
    private static int Counter = 10;
    private static float Elapsed;

    public int visibleCounter;
    public float visibleElapsed;

    private void Update()
    {
        Counter++;
        Elapsed += Time.deltaTime;

        visibleCounter = Counter;
        visibleElapsed = Elapsed;
    }
}
```

Lowered shape:

```csharp
private K13A.UdonStatic.Runtime.UdonStaticGlobalStore __udonStaticStore;

private K13A.UdonStatic.Runtime.UdonStaticGlobalStore __UdonStatic_GetStore()
{
    if (__udonStaticStore == null)
        __udonStaticStore = UnityEngine.GameObject.Find("__UdonStaticGlobalStore")
            .GetComponent<K13A.UdonStatic.Runtime.UdonStaticGlobalStore>();

    return __udonStaticStore;
}
```

`Counter` becomes `__UdonStatic_GetStore().IntData[index]`, `Elapsed` becomes `__UdonStatic_GetStore().FloatData[index]`, and every instance resolves the same singleton store object.

Supported MVP:

- Direct `UdonSharpBehaviour` classes.
- Simple static fields declared in UdonSharp behaviour classes.
- Same-class access such as `Counter++`.
- Class-qualified access such as `Example.Counter = 10`, including from another UdonSharp behaviour compiled in the same pass.
- Field initializers for common literal values.
- One scene singleton object named `__UdonStaticGlobalStore`.

Data storage:

- `IntData`, `FloatData`, `BoolData`, `StringData`
- `LongData`, `DoubleData`
- `Vector2Data`, `Vector3Data`, `QuaternionData`, `ColorData`
- `GameObjectData`, `TransformData`
- `ObjectData` UnityEngine.Object fallback

Limitations:

- This is still emulation on top of one UdonBehaviour heap, not native CLR static memory.
- Static constructors, static methods, properties, events, generic static fields, nested classes, and advanced initialization order are not implemented.
- Complex non-literal initializers default to the type default in the generated store.
- The store is scene-local. Cross-scene persistence is outside the current scope.

Examples:

- `Examples/UdonStaticCounterExample.cs`: static fields shared through the global store and updated every frame.

Editor tests:

- `Editor/Tests/UdonStaticLowererTests.cs`: validates global store lowering, same-class access, cross-class access, local shadowing, and ignored non-UdonSharp classes.
