# Android 개발 브랜치 변경 사항

기준: `main` 브랜치 `5f88231`
작성일: 2026-11-07
대상 브랜치: `Android_Dev`

이 문서는 기존 `main`과 비교해 Android 기기 실행과 파일럿 테스트를 위해 추가하거나
변경한 구현을 정리한다. 기존 거울 장면, 도구 모델, iOS CoreHaptics 구현은 `main`의
구조를 유지한다.

## 1. Android 빌드 설정

- Android Application Entry를 `GameActivity`에서 `Activity`로 변경했다.
- Vulkan pre-transform을 비활성화했다.
- ARM64 Android 빌드 구성을 유지한다.
- 커스텀 `mainTemplate.gradle`과 `gradleTemplate.properties`를 사용한다.
- 기존 Android 프로젝트에서 사용하던 다음 의존성을 Gradle 템플릿에 반영했다.
  - AndroidX AppCompat 1.6.1
  - Google Play Services Vision 20.1.3
  - Material Components 1.12.0
  - protobuf-javalite 3.19.4
  - jspecify 1.0.0
- PC Haptic Link의 TCP 수신을 위해 Android 인터넷 권한을 강제 포함한다.
- Unity 6 Android Build Profile을 추가했다.

관련 파일:

- `ProjectSettings/ProjectSettings.asset`
- `Assets/Plugins/Android/mainTemplate.gradle`
- `Assets/Plugins/Android/gradleTemplate.properties`
- `Assets/Settings/Build Profiles/Android™.asset`

## 2. 실행 시작 흐름과 통합 설정 메뉴

앱 실행 직후 여러 진동을 자동으로 재생하던 자가진단과 10개 패턴 자동 비교를 기본값에서
비활성화했다. 대신 XR을 잠시 중지하고 다음 항목을 선택할 수 있는 통합 설정 메뉴를 연다.

- Preview Pattern
- Controller Setup
- Mirror Scene
- Test Haptic Directly on PC
- Start Experiment

`Compare all 10` 버튼은 제거했다. `Start Experiment`는 아직 기존 360도 탐색 실험으로
연결된다.

XR이 중지된 설정 화면에서는 Cardboard API를 호출하지 않는다. 이로써 Android 로그에
`Please initialize Cardboard XR loader`가 매 프레임 출력되던 문제를 막았다.

관련 파일:

- `Assets/PatternSettingsMenu.cs`
- `Assets/ExperimentController.cs`

## 3. Preview Pattern

- 기존 10개 `VibrationPattern`을 개별 선택한다.
- `Play this pattern` 버튼을 파라미터 입력 영역 위로 이동했다.
- Stop 버튼을 제거하고 반복 횟수 입력칸을 추가했다.
- 반복 횟수는 1~100이다.
- 새 Play를 누르거나 다른 페이지로 이동하면 이전 반복 재생을 중단한다.
- 반복 간격의 의미를 시작 시점 간격에서 **패턴 종료 후 추가 대기시간**으로 변경했다.
- `Post-pattern gap` 범위는 20~5000ms이다.
- 패턴마다 전체 파형 길이를 계산한 뒤 `파형 길이 + post-pattern gap` 후 다음 재생을
  시작한다.

파형 길이 계산은 Android와 iOS가 공유하는 `HapticManager.GetPatternDurationMs()`를
사용한다.

## 4. Bluetooth 컨트롤러 진단

`ControllerConnectionDiagnostics`가 장면 로드 후 자동 설치된다. 별도의 Scene 오브젝트
설정 없이 연결된 Gamepad/Joystick/controller-like HID를 검색하고 다음 정보를 제공한다.

- 장치 ID, layout, display name, interface, manufacturer, product
- 실제 버튼 control path
- 왼쪽 스틱 Vector2 값
- 최근 입력 로그
- North/South/West/East 버튼의 현재 상태

확인된 Android 장치 매핑:

- 장치 표시: `SM-031 Keyboard`
- layout: `XboxOneGamepadAndroid`
- 버튼: `buttonNorth`, `buttonSouth`, `buttonWest`, `buttonEast`
- 스틱: `leftStick`

스틱은 `leftStick`의 Vector2 값만 사용한다. Input System이 자동 생성한
`leftStick/up`, `leftStick/down`, `leftStick/left`, `leftStick/right` 자식 버튼은 실제
조작 로직에서 사용하지 않는다.

관련 파일:

- `Assets/ControllerConnectionDiagnostics.cs`
- `Assets/PatternSettingsMenu.cs`

## 5. Mirror Scene 선택 및 컨트롤러 조작

설정 메뉴에서 Hair Clipper, Eating, Dental Drill 중 하나를 선택해 진입한다. 기존처럼
세 장면을 Cardboard 트리거로 순서대로 넘기지 않는다.

Android Gamepad 조작:

| 입력 | 동작 |
|---|---|
| South | 애니메이션 재생/일시정지 |
| West | 이전 진동 패턴 |
| North | 다음 진동 패턴 |
| East | 미러 씬 종료 |
| 왼쪽 스틱 위/아래 | intensity를 5씩 변경, 범위 1~255 |
| 왼쪽 스틱 왼쪽/오른쪽 | post-pattern gap을 20ms씩 변경, 범위 20~5000ms |

사용할 수 있는 진동 모드는 다음 13개다.

- Continuous
- Clipper Load
- Crunch
- 기존 `VibrationPattern` 10개

HUD에는 시나리오, 재생 상태, 현재 패턴, intensity, post-pattern gap, 컨트롤러 연결
상태를 표시한다. 10개 `VibrationPattern`은 실제 파형 종료 후 post-pattern gap을 기다린
뒤 반복한다. Continuous, Clipper Load, Crunch에는 post-pattern gap이 적용되지 않는다.

Cardboard 화면 탭은 컨트롤러 연결이 끊겼을 때의 종료 수단으로 유지한다.

관련 파일:

- `Assets/ExperimentController.cs`
- `Assets/MirrorRig.cs`
- `Assets/HapticManager.cs`

## 6. Mirror Scene 재생 제어

`MirrorRig`에 `IsPlaying`과 `SetPlaying()`을 추가했다. 일시정지 중에는 애니메이션 시간과
접촉값이 진행되지 않으며, 접촉 햅틱도 중단된다.

미러 장면 애니메이션 자체는 기존처럼 `Animator`가 아닌 `LateUpdate()` 기반 절차적
애니메이션이다. 현재 앱의 정상 진입 흐름은 Cardboard XR Loader 시작을 요구한다.

## 7. PC Haptic Link

별도 Android 앱 없이 Unity Editor에서 작성한 파형을 USB로 연결된 휴대폰에서 즉시
재생할 수 있다.

동작 순서:

1. Android 앱에서 `Test Haptic Directly on PC` 페이지를 연다.
2. Unity에서 `Tools > Haptic Waveform Designer`를 연다.
3. `ADB Forward`로 `tcp:48700`을 휴대폰의 같은 포트에 전달한다.
4. duration/amplitude 배열을 입력하고 `Play on Phone`을 누른다.

모바일 수신기는 해당 설정 페이지가 열려 있을 때만 동작한다. 앱이 Pause되면 포트를
닫고, Resume되었을 때 페이지가 여전히 활성 상태면 자동으로 다시 연다.

초기 구현은 ADB 로컬 포트에 쓰기만 해 실제 모바일 수신 실패도 성공으로 표시할 수
있었다. 현재는 모바일이 JSON ACK를 반환하고 Editor가 ACK를 확인한 경우에만
`Phone ACK: ...` 성공 메시지를 표시한다.

안전 제한:

- 최대 256개 segment
- 전체 길이 최대 10초
- segment 길이 1~2000ms
- amplitude 0~255
- PC Haptic Link 페이지를 나가면 수신기와 재생을 중단

관련 파일:

- `Assets/HapticLinkReceiver.cs`
- `Assets/Editor/HapticWaveformDesignerWindow.cs`
- `ProjectSettings/ProjectSettings.asset`

## 8. 진동 파라미터 동작

- Android amplitude/intensity 범위는 1~255다.
- 미러 씬 조이스틱은 로컬 `intensity`를 변경하며 `waveHighAmplitude`에 다시 저장하지
  않는다.
- 기존 10개 패턴의 실효 peak는 대략
  `waveHighAmplitude × intensity / 255`다.
- Continuous는 로컬 intensity를 직접 사용한다.
- Crunch는 `ScenarioHaptics.PlayCrunch(4, intensity, seed)`로 intensity를 peak로 직접
  사용하고 접촉 시작 시 한 번 재생한다.
- `patternRepeatMs` 필드 이름은 직렬화 호환을 위해 유지하지만, 현재 의미는
  post-pattern gap이다.

## 9. 현재 제한 및 후속 작업

- Unity Editor에서 일반 앱 흐름으로 Mirror Scene을 실시간 실행하려면 Cardboard XR
  우회용 Editor 경로가 추가로 필요하다.
- 손 위치와 손가락 모양은 절차적 IK가 매 프레임 덮어쓰므로 Inspector 직접 이동으로
  저장할 수 없다. grip/curl/pole 값을 노출하는 별도 포즈 튜너가 필요하다.
- PC Haptic Link는 쉼표로 구분한 duration/amplitude 배열을 입력하는 1차 구현이다.
  그래프 기반 파형 편집기는 아직 없다.
- 조이스틱으로 조정한 intensity와 post-pattern gap은 현재 미러 씬 로컬 값이며 설정
  파일이나 결과 CSV에 저장하지 않는다.
- `Start Experiment`는 새 파일럿 프로토콜이 아니라 기존 탐색 실험을 실행한다.

## 10. 검증 상태

- Android APK 빌드 및 `UnityPlayerActivity` 실행 확인
- API 26, ARM64, Gradle 의존성 포함 확인
- Android에서 `SM-031 Keyboard / XboxOneGamepadAndroid` 입력 확인
- 런타임 C# 컴파일: 경고 0, 오류 0
- Editor Haptic Designer 컴파일 확인
- Editor의 기존 `CIBuild.cs`에는 XR Management의 폐기 예정 API 경고가 남아 있다.
