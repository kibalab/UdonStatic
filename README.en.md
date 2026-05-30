[한국어](README.md) | **English** | [日本語](README.ja.md)

---

# UdonStatic

## Introduction

UdonStatic is an experimental extension package that lets UdonSharp code use scene-wide shared values with syntax similar to C# `static` fields.

## Purpose

The goal is to share values between multiple UdonSharpBehaviour instances without manually writing a separate manager Behaviour for every shared counter, flag, timer, or object reference.

Example source:

```csharp
using UdonSharp;
using UnityEngine;

public class CounterExample : UdonSharpBehaviour
{
    private static int Counter = 0;
    private static float ElapsedSeconds = 0f;

    public int visibleCounter;
    public float visibleElapsedSeconds;

    private void Update()
    {
        Counter++;
        ElapsedSeconds += Time.deltaTime;

        visibleCounter = Counter;
        visibleElapsedSeconds = ElapsedSeconds;
    }
}
```

## How It Works

Udon and the VRChat runtime do not expose normal CLR static memory. UdonStatic creates one scene singleton UdonBehaviour named `__UdonStaticGlobalStore` and rewrites static field declarations and accesses before UdonSharp compilation so the values are stored in typed data arrays on that singleton.

For example, an access like `Counter++` is rewritten before compilation into a form like `__UdonStatic_GetStore().IntData[index]++`. Every UdonSharpBehaviour instance that refers to the same static field uses the same storage slot.

## Installation

Install through VCC or VPM.

Install link:

- https://vpm.kiba.red/

Package ID:

```text
com.kibalab.udonstatic
```

Required VPM dependency:

```json
{
  "vpmDependencies": {
    "com.vrchat.worlds": ">=3.9.0"
  }
}
```

Release tags must match the `version` field in `package.json`, for example `0.1.0` or `v0.1.0`.

## Usage And Examples

Declare a normal static field inside an UdonSharpBehaviour.

```csharp
using UdonSharp;
using UnityEngine;

public class SharedScore : UdonSharpBehaviour
{
    public static int Score = 10;
    public int visibleScore;

    private void Update()
    {
        Score++;
        visibleScore = Score;
    }

    public override void Interact()
    {
        SharedScore.Score = 0;
    }
}
```

During compilation, UdonStatic removes the static field declaration and rewrites all accesses to array slots on the scene `__UdonStaticGlobalStore` object.

Supported storage types:

- `int`, `float`, `bool`, `string`
- `long`, `double`
- `Vector2`, `Vector3`, `Quaternion`, `Color`
- `GameObject`, `Transform`, `UnityEngine.Object`

Included example:

- `Packages/com.kibalab.udonstatic/Examples/UdonStaticCounterExample.cs`

## Notes And Limitations

- This is an emulation layer on top of one UdonBehaviour heap, not native CLR static memory.
- Storage is scene-local. Cross-scene persistence is not provided.
- Static constructors, static properties, events, generic static fields, and complex initialization order are not supported yet.
- The main supported target is simple static fields declared inside `UdonSharpBehaviour` classes.
- Static field initializers are handled for literals and a few Unity default values. Complex initializers may be synchronized as the type default value.
- The package extends UdonSharp compiler internals through Harmony patches, so compatibility should be checked after VRChat SDK or UdonSharp updates.

## Contribute

Bug reports, minimal reproduction projects, VRChat SDK version information, and Unity Console logs are welcome.

When contributing:

- Keep package changes under `Packages/com.kibalab.udonstatic` whenever possible.
- Add lowerer tests under `Editor/Tests` for new syntax support when practical.
- For UdonSharp compiler fixes, verify the change in an actual Unity project with an UdonSharp compile.

README and release documentation are kept aligned during review.

To release, bump `version` in `package.json` and push a Git tag with the same version. GitHub Actions will generate the VPM release artifacts.
