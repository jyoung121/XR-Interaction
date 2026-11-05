# XR Interaction — 머리 착용 진동의 현실감 지각

DGIST IC527 텀프로젝트 6조.

머리에 찬 진동 장치가 **"지금 내 머리/입에 뭔가 닿고 있다"로 느껴지려면
진동이 어때야 하는가**를 재는 Cardboard VR 앱이다. 참가자는 세 가지 상황을
보면서 진동 파라미터를 직접 조절하고, 그 값을 기록한다.

> 중간고사(10/13) 전까지는 360° 탐색 과제에 진동 안내를 주는 주제였다.
> 그 코드는 아직 저장소에 남아 있다 — 아래 "남은 것" 참고.

## 세 가지 상황

| | 접촉 지점 | 장면 |
|---|---|---|
| 바리깡 | 머리 측면 (관자놀이) | 미용실. 넓은 벽거울, 뒤에 선 미용사가 밀어 준다 |
| 음식 | 입 | 집. 전신 거울, 본인 손으로 사과를 먹는다 |
| 치과 | 입 | 치과. 누운 자세, 머리 위 치료등에 달린 둥근 거울 |

머리와 입을 나눈 건 접촉 지점에 따라 측정값이 달라지는지 보기 위해서다.

## 왜 거울인가

순수 1인칭에서는 내 머리도 입도 시야 밖이라, 진동이 **어디에** 닿는지가
눈에 안 보인다. 그래서 시점은 1인칭 그대로 두고(고개를 숙이면 본인 몸이
보인다) 눈앞에 거울을 하나 더 뒀다.

거울은 RenderTexture 가 아니라 **거울면 뒤에 방을 한 벌 더 지어서** 만든다.
z 를 `2D - z` 로 뒤집어 실제 도형을 놓으므로 양쪽 눈의 깊이가 정확히 맞는다.
RenderTexture 로는 한쪽 눈 기준으로만 맞아서 Cardboard 에서 어긋난다.

설계 근거와 구현 중 걸렸던 것들은 [docs/비주얼설계.md](docs/비주얼설계.md) 에
적어 뒀다 — 둥근 거울을 기본 도형으로 만드는 법, 거울 속 조명을 짝짓는 법,
팔 IK 와 손 모양, 각 시행착오의 원인까지.

## 환경

- Unity 6000.6.7f1
- Google Cardboard XR Plugin 1.36.0
- Android (VibrationEffect) / iOS (CoreHaptics)

## 빌드

**iOS** — macOS 가 없으므로 세 단계로 나눈다.

1. 윈도우에서 Xcode 프로젝트 생성 → `iOSBuild.zip`
   ```
   Unity.exe -batchmode -quit -projectPath . -buildTarget iOS -executeMethod CIBuild.BuildIOS
   ```
   iOS 모듈이 깔린 설치본으로 해야 한다.
2. 그 zip 을 이 저장소 Release 의 `xcode-latest` 에 첨부한 뒤,
   Actions 의 `Build unsigned iOS .ipa` 워크플로를 돌린다.
   첨부가 100% 된 다음 **Update release 를 누르지 말 것** — 누르면 방금 올린
   파일이 지워진다.
3. 나온 `.ipa` 를 Sideloadly 로 무료 Apple ID 서명해 설치한다. 7일마다 재설치.

**Android** — `CIBuild.BuildAndroid`. Unity Hub 에서 Android Build Support 를
SDK / NDK / JDK 까지 빠짐없이 설치해야 한다. 조각이 빠져 있으면 패키지 컴파일
오류나 "Android SDK not found" 로 떨어진다.

## 주요 파일

```
Assets/MirrorRig.cs               방·거울·등장인물·도구를 런타임에 세운다 (핵심)
Assets/ToolModels.cs              바리깡·사과·드릴 모델, 런타임 재질
Assets/ScenarioHaptics.cs         상황별 진동 패턴
Assets/HapticManager.cs           Android/iOS 공통 진동 창구
Assets/AndroidVibrationManager.cs Android VibrationEffect
Assets/Plugins/iOS/CardboardHaptics.mm   iOS CoreHaptics
Assets/Resources/Models/          Person.fbx (Quaternius, CC0), Apple/Cookie.obj
Assets/Resources/Materials/RuntimeBase.mat   런타임 재질이 쓸 URP 셰이더
Assets/Editor/CIBuild.cs          명령줄 빌드 진입점
Assets/Editor/ToolPreview.cs      빌드 전에 오프스크린으로 찍어 확인
Assets/Editor/IOSBuildPostProcess.cs     CoreHaptics 프레임워크 연결
Assets/ExperimentController.cs    예전 탐색 실험 러너 (교체 예정)
```

`Resources/Materials/RuntimeBase.mat` 과 Always Included Shaders 의 URP/Lit 은
지우면 안 된다. 이게 없으면 아이폰 빌드에서 런타임 재질이 전부 오류
셰이더(분홍색)로 나온다. 이 프로젝트는 URP 를 품질 단계에만 걸어 둬서
에디터에서는 이 차이가 드러나지 않는다.

## 현재 상태 (2026-11-05)

세 상황의 방·거울·등장인물·도구 구현을 마쳤고, 아이폰 실기에서 세 장면 모두
정상 동작하는 것을 확인했다. 고개를 숙이면 본인 몸이 보이는 것도 확인했다.

## 남은 것

- 참가자용 조절 화면 (VR 안에서 파라미터를 올리고 내리는 UI) — 블루투스
  컨트롤러가 있어야 진행 가능
- 결과 CSV 를 새 설계로 교체: `참가자 / 상황 / 파라미터 / 방향 / 역전값 / 반복`
  (현재는 예전 탐색 실험의 20열 형식)
- 메뉴의 `Start experiment` 가 아직 예전 탐색 실험으로 연결돼 있다
- 새 주제에 맞는 선행 연구 다시 정리

## 일정

M2 Build — 2026-11-17
