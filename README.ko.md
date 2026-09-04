# LLM2PCG

[English](README.md) | [한국어](README.ko.md)

LLM2PCG는 JSON 요청을 기반으로 여러 형태의 월드를 생성하는 결정론적 절차 생성(PCG) 프로젝트입니다. 같은 요청과 seed를 사용하면 동일한 월드 데이터와 해시를 재현할 수 있으며, 결과는 브라우저 또는 Unity 6에서 확인할 수 있습니다.

## 아키텍처

```text
자연어 프롬프트(선택적 OpenAI API 연동)
                         |
                         v
브라우저 UI ---> 검증된 공용 JSON 계약
                         |
              +----------+----------+
              |                     |
              v                     v
       .NET CoreHost          Unity 로컬 Bridge
       순수 생성 알고리즘      메인 스레드 전달
              |                     |
              v                     v
       Three.js 렌더러        Unity 메시/인스턴싱 렌더러
              |                     |
              +----------+----------+
                         v
           결정적 월드 해시와 재현 가능한 결과
```

지원 월드: Dungeon, Cave, Forest, City, Swamp, Snowfield, Desert.

## 요구 사항

- Git
- .NET 8 SDK
- Node.js 20 이상
- PowerShell 7 권장
- Unity 데모용 Unity `6000.5.10f1` — 독립 실행형 브라우저 데모에는 Unity가 필요하지 않습니다

## Clone 및 검증

```powershell
git clone https://github.com/heparidayo/llm2pcg.git
cd llm2pcg
pwsh -File ./Scripts/Test-Clone.ps1
```

검증 스크립트는 lockfile에 고정된 Web 의존성을 설치하고, .NET host 빌드, 결정론적 CoreHost 검사, Web 테스트와 일곱 월드 브라우저/API smoke test를 실행합니다.

## 브라우저 데모 실행

```powershell
pwsh -File ./Scripts/Start-StandaloneWeb.ps1
```

브라우저에서 `http://127.0.0.1:3000`을 엽니다. 기본 preset/direct 생성 과정은 로컬에서 동작하며 API key가 필요하지 않습니다. 두 프로세스를 종료하려면 터미널에서 `Ctrl+C`를 누릅니다.

선택적인 자연어 요청 변환 기능을 사용하려면 `Web/.env.example`을 `Web/.env`로 복사하고 본인의 server-side key를 입력하세요. 이 파일은 절대 commit하지 마세요.

## Unity 데모 실행

1. Unity Hub에서 clone한 저장소의 `UnityDemo` 폴더를 추가합니다.
2. `Assets/PCGPublicDemo/Scenes/PrimitiveDemo.unity`를 엽니다.
3. Play Mode로 진입합니다.
4. 화면의 world type과 seed를 선택합니다. `F1`을 누르면 1인칭 테스트 모드로 전환됩니다.

샘플은 실행 중 카메라, 조명, 생성 pipeline과 fallback material을 구성합니다. 외부 아트 에셋은 필요하지 않습니다. HTTP Bridge는 패키지에서 기본 비활성화되어 있고 loopback 주소만 사용하며, 이 로컬 데모 씬이 `8088` 포트에서 명시적으로 시작합니다.

## Unity 패키지로 재사용

다음 형식의 Git URL을 사용하면 Unity Package Manager에서 패키지 하위 폴더만 설치할 수 있습니다. 특정 버전을 사용하려면 URL 끝의 `<tag>`를 실제 tag로 바꿉니다.

```text
https://github.com/heparidayo/llm2pcg.git?path=/Packages/com.heparidayo.llm2pcg#<tag>
```

또는 clone한 저장소의 `Packages/com.heparidayo.llm2pcg`를 local package로 추가할 수 있습니다.

## 저장소 구성

- `Packages/com.heparidayo.llm2pcg`: 재사용 가능한 Unity runtime과 EditMode 테스트
- `UnityDemo`: 외부 에셋 없는 Unity 데모 프로젝트
- `Standalone`: Unity 없이 생성 가능한 .NET 8 host
- `Web`: Three.js 브라우저 렌더러와 선택적 prompt adapter
- `Shared`: version이 명시된 JSON Schema와 JavaScript validator/default
- `Scripts`: clone 검증과 로컬 실행 스크립트
