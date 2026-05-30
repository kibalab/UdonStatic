[한국어](README.md) | [English](README.en.md) | **日本語**

---

# UdonStatic

## 紹介

UdonStatic は、UdonSharp で C# の `static` field に近い構文を使い、シーン全体で共有される値を扱えるようにする実験的な拡張パッケージです。

## 目的

複数の UdonSharpBehaviour インスタンス間で値を共有したいときに、共有カウンター、フラグ、タイマー、オブジェクト参照のためだけに毎回専用の manager Behaviour を書かなくてもよくすることが目的です。

例:

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

## 仕組み

Udon / VRChat ランタイムは通常の CLR static memory をそのまま提供しません。UdonStatic はシーン内に `__UdonStaticGlobalStore` というシングルトン UdonBehaviour を作成し、UdonSharp のコンパイル前に static field の宣言とアクセスを、そのシングルトン上の typed data array へのアクセスに変換します。

たとえば `Counter++` のようなアクセスは、コンパイル前に `__UdonStatic_GetStore().IntData[index]++` のような形へ変換されます。同じ static field を参照するすべての UdonSharpBehaviour インスタンスは、同じ保存スロットを使用します。

## インストール

VCC または VPM からインストールします。

インストールリンク:

- https://vpm.kiba.red/

パッケージ ID:

```text
com.kibalab.udonstatic
```

必須 VPM 依存関係:

```json
{
  "vpmDependencies": {
    "com.vrchat.worlds": ">=3.9.0"
  }
}
```

リリースタグは `package.json` の `version` と一致している必要があります。例: `0.1.0` または `v0.1.0`。

## 使い方と例

UdonSharpBehaviour の中に通常の static field を宣言します。

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

コンパイル時に、UdonStatic は static field 宣言を削除し、すべてのアクセスをシーン内の `__UdonStaticGlobalStore` オブジェクト上の配列スロットへのアクセスに変換します。

対応している保存型:

- `int`, `float`, `bool`, `string`
- `long`, `double`
- `Vector2`, `Vector3`, `Quaternion`, `Color`
- `GameObject`, `Transform`, `UnityEngine.Object`

同梱例:

- `Packages/com.kibalab.udonstatic/Examples/UdonStaticCounterExample.cs`

## 注意事項

- これはネイティブな CLR static memory ではなく、1つの UdonBehaviour heap 上でのエミュレーションです。
- 保存領域はシーン単位です。シーンをまたいだ永続化は提供しません。
- static constructor、static property、event、generic static field、複雑な初期化順序はまだ対応していません。
- 主な対応対象は `UdonSharpBehaviour` クラス内に宣言された単純な static field です。
- static field の初期値は literal と一部の Unity default value を中心に処理します。複雑な initializer は型の default value として同期される場合があります。
- このパッケージは Harmony patch で UdonSharp の内部コンパイラ API を拡張するため、VRChat SDK / UdonSharp の更新後は互換性確認が必要です。

## Contribute

バグ報告、最小再現プロジェクト、VRChat SDK バージョン情報、Unity Console ログを含む issue を歓迎します。

コントリビューション時は以下を確認してください。

- 可能な限り `Packages/com.kibalab.udonstatic` 以下のパッケージファイルだけを変更してください。
- 新しい構文対応を追加する場合、可能であれば `Editor/Tests` に lowerer テストを追加してください。
- UdonSharp コンパイル関連の修正では、実際の Unity プロジェクトで UdonSharp compile まで確認してください。

README とリリース文書は、レビューの過程でパッケージの状態に合わせて整えます。

リリースするには、`package.json` の `version` を上げ、同じバージョンの Git タグを push します。GitHub Actions が VPM 配布用アーティファクトを生成します。
