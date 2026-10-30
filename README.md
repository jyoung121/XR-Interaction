# XR Interaction — Cardboard Haptic Search

Google Cardboard에서 360° 탐색 과제를 수행하며, 시각 화살표에 더해
진동 안내(주기 변조 / 세기 변조)가 탐색 성능에 주는 영향을 측정하는 실험 앱.

## 조건

| | 시각 | 진동 |
|---|---|---|
| Case 1 | 화살표 | 없음 (베이스라인) |
| Case 2 | 화살표 | 가까울수록 빠르게 (주기 변조) |
| Case 3 | 화살표 | 가까울수록 세게 (세기 변조) |

## 환경

- Unity 6000.6.7f1
- Google Cardboard XR Plugin 1.36.0
- Android (VibrationEffect) / iOS (CoreHaptics) 양쪽 지원

## 빌드

**Android** — Unity에서 바로 APK 빌드.

**iOS** — macOS가 필요하므로 두 단계로 나눈다.
1. 윈도우에서 Unity로 Xcode 프로젝트 생성 → `iOSBuild.zip` 으로 압축
2. 그 zip을 이 저장소의 Release에 첨부한 뒤, Actions의
   `Build unsigned iOS .ipa` 워크플로를 돌리면 서명 없는 `.ipa` 가 나온다
3. 받은 `.ipa` 를 Sideloadly로 무료 Apple ID 서명해 기기에 설치

## 주요 파일

```
Assets/ExperimentController.cs    실험 러너 (시행 진행, 이심각 통제, CSV 기록)
Assets/HapticManager.cs           Android/iOS 공통 진동 창구
Assets/AndroidVibrationManager.cs Android VibrationEffect
Assets/Plugins/iOS/CardboardHaptics.mm   iOS CoreHaptics
Assets/Editor/CIBuild.cs          명령줄 빌드 진입점
Assets/Editor/IOSBuildPostProcess.cs     CoreHaptics 프레임워크 연결
```
