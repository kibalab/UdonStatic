**한국어** | [English](README.en.md) | [日本語](README.ja.md)

---

# UdonStatic

## 소개

UdonStatic은 UdonSharp에서 C# `static` field와 비슷한 문법으로 씬 전역 공유 값을 사용할 수 있게 하는 실험적 확장 패키지입니다.

## 목적

여러 UdonSharpBehaviour 인스턴스가 같은 값을 공유해야 할 때 매번 별도 매니저 Behaviour를 직접 만들지 않고, 일반 C# static field에 가까운 문법으로 상태를 다룰 수 있게 하는 것이 목적입니다.

예를 들어 여러 오브젝트가 같은 카운터, 플래그, 타이머, 참조 값을 공유해야 하는 경우 다음과 같은 코드를 작성할 수 있습니다.

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

## 원리

Udon/VRChat 런타임은 일반적인 CLR static memory를 그대로 제공하지 않습니다. UdonStatic은 씬 안에 `__UdonStaticGlobalStore`라는 싱글톤 UdonBehaviour를 만들고, UdonSharp 컴파일 전에 static field 선언과 접근을 해당 저장소의 typed data array 접근으로 변환합니다.

예를 들어 `Counter++` 같은 접근은 컴파일 전에 `__UdonStatic_GetStore().IntData[index]++` 형태로 바뀝니다. 같은 static field를 참조하는 모든 UdonSharpBehaviour 인스턴스는 같은 저장소 슬롯을 사용합니다.

## 설치

VCC 또는 VPM으로 설치합니다.

설치 링크:

- https://vpm.kiba.red/

패키지 ID:

```text
com.kibalab.udonstatic
```

필수 VPM 의존성:

```json
{
  "vpmDependencies": {
    "com.vrchat.worlds": ">=3.9.0"
  }
}
```

릴리스 태그는 `package.json`의 `version`과 일치해야 합니다. 예: `0.1.0` 또는 `v0.1.0`.

## 사용법 및 예제

UdonSharpBehaviour 안에 일반 static field를 선언합니다.

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

컴파일 시 UdonStatic은 static field를 제거하고, 모든 접근을 씬의 `__UdonStaticGlobalStore` 오브젝트에 있는 배열 접근으로 바꿉니다.

지원하는 저장소 타입:

- `int`, `float`, `bool`, `string`
- `long`, `double`
- `Vector2`, `Vector3`, `Quaternion`, `Color`
- `GameObject`, `Transform`, `UnityEngine.Object`

패키지 안의 예제:

- `Packages/com.kibalab.udonstatic/Examples/UdonStaticCounterExample.cs`

## 주의사항

- 이 기능은 네이티브 CLR static memory가 아니라 UdonBehaviour heap 위의 에뮬레이션입니다.
- 저장소는 씬 단위입니다. 씬을 넘어 지속되는 전역 저장소는 제공하지 않습니다.
- static constructor, static property, event, generic static field, 복잡한 초기화 순서 처리는 아직 지원하지 않습니다.
- 현재 주요 지원 대상은 `UdonSharpBehaviour` 클래스 안에 선언된 단순 static field입니다.
- static field 초기값은 literal 및 일부 Unity 기본값 위주로 처리됩니다. 복잡한 initializer는 타입 기본값으로 동기화될 수 있습니다.
- UdonSharp 내부 컴파일러 API를 Harmony patch로 확장하므로, VRChat SDK/UdonSharp 업데이트에 따라 호환성 확인이 필요합니다.

## 기여 (Contribute)

버그 리포트, 재현 가능한 예제, VRChat SDK 버전 정보, Unity Console 로그를 포함한 이슈를 환영합니다.

기여할 때는 다음을 확인해 주세요.

- `Packages/com.kibalab.udonstatic` 아래의 패키지 파일만 변경했는지 확인합니다.
- 새 기능에는 가능한 경우 `Editor/Tests`에 lowerer 테스트를 추가합니다.
- UdonSharp 컴파일 오류를 수정하는 경우, 실제 Unity 프로젝트에서 UdonSharp compile까지 확인합니다.

README와 릴리스 문서는 리뷰 과정에서 패키지 상태에 맞게 정리합니다.

릴리스는 `package.json`의 `version`을 올린 뒤 같은 버전의 Git 태그를 푸시하면 GitHub Actions가 VPM 배포용 아티팩트를 생성합니다.
