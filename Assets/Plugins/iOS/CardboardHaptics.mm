// CardboardHaptics.mm
// iOS 진동 래퍼 — Unity 에서 C 함수로 호출한다.
//
// Android 의 VibrationEffect 와 맞추기 위해 두 가지를 제공한다.
//   1) 짧은 펄스 (Case 2 — 주기 변조)        -> ch_transient
//   2) 세기를 실시간으로 바꾸는 연속 진동 (Case 3) -> ch_beginContinuous / ch_setContinuousIntensity
//
// 진동 경로는 세 단계로 떨어진다.
//   CoreHaptics      — 세기를 자유롭게 지정. Case 2·3 모두 가능 (iOS 13+, iPhone 8+)
//   UIImpactFeedback — 세기 3단계. Case 2 는 가능, Case 3 는 불가
//   AudioServices    — 세기·길이 고정. 기기가 진동 자체는 되는지 가리는 용도
//
// 맥이 없어 Xcode 콘솔을 볼 수 없으므로, 실패 원인을 ch_diagnostics() 로 C# 에 돌려준다.
// 이게 없으면 모든 실패가 NSLog 로만 사라져 원인을 알 수 없다.

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <CoreHaptics/CoreHaptics.h>
#import <AudioToolbox/AudioToolbox.h>

// ───────────────────────── 상태 ─────────────────────────

static id g_engine     = nil;    // CHHapticEngine *
static id g_continuous = nil;    // id<CHHapticAdvancedPatternPlayer>

static BOOL g_hwSupports = NO;   // 하드웨어가 CoreHaptics 를 지원하는가
static BOOL g_running    = NO;   // 엔진이 지금 돌고 있는가

// 어느 경로로 몇 번 울렸는지 — HUD 로 확인한다
static int g_coreOk   = 0;
static int g_coreFail = 0;
static int g_impactOk = 0;
static int g_audioOk  = 0;

static char g_lastErr[192] = "";
static char g_diag[384]    = "";

static void SetErr(NSString *s)
{
    if (s == nil) return;
    snprintf(g_lastErr, sizeof(g_lastErr), "%s", [s UTF8String]);
    NSLog(@"[CardboardHaptics] %@", s);
}

// ───────────────────────── 엔진 ─────────────────────────

/// 엔진을 만들고, 멈춰 있으면 다시 켠다.
///
/// 이전 버전은 g_engine 이 nil 이 아니면 곧바로 성공으로 돌려줬다.
/// 그래서 엔진이 한 번 정지하면(앱이 뒤로 갔다 오거나 오디오 세션이 끊기면)
/// 그 뒤의 모든 호출이 조용히 무시됐다. 여기서는 매번 실행 여부를 확인한다.
static BOOL EnsureRunning(void)
{
    if (@available(iOS 13.0, *)) {
        g_hwSupports = CHHapticEngine.capabilitiesForHardware.supportsHaptics ? YES : NO;
        if (!g_hwSupports) {
            SetErr(@"hw: supportsHaptics=NO");
            return NO;
        }

        NSError *error = nil;

        if (g_engine == nil) {
            CHHapticEngine *engine = [[CHHapticEngine alloc] initAndReturnError:&error];
            if (engine == nil) {
                SetErr([NSString stringWithFormat:@"init: %@", error.localizedDescription]);
                return NO;
            }

            engine.playsHapticsOnly    = YES;   // 오디오 세션을 건드리지 않는다
            engine.autoShutdownEnabled = NO;    // 쉬는 동안 꺼지지 않게

            // 정지·리셋되면 다시 켜야 한다는 사실을 남긴다.
            // 이 블록은 임의의 큐에서 불리므로 여기서 직접 start 하지 않고
            // 플래그만 내리고, 다음 호출에서 EnsureRunning 이 켠다.
            engine.stoppedHandler = ^(CHHapticEngineStoppedReason reason) {
                g_running    = NO;
                g_continuous = nil;
                snprintf(g_lastErr, sizeof(g_lastErr), "stopped reason=%ld", (long)reason);
            };
            engine.resetHandler = ^{
                g_running    = NO;
                g_continuous = nil;
                snprintf(g_lastErr, sizeof(g_lastErr), "engine reset");
            };

            g_engine = engine;
        }

        if (!g_running) {
            error = nil;
            [(CHHapticEngine *)g_engine startAndReturnError:&error];
            if (error != nil) {
                SetErr([NSString stringWithFormat:@"start: %@", error.localizedDescription]);
                return NO;
            }
            g_running = YES;
        }
        return YES;
    }

    SetErr(@"iOS 13 미만");
    return NO;
}

// ───────────────────────── 폴백 ─────────────────────────

/// UIImpactFeedbackGenerator — CoreHaptics 보다 단순하고 잘 깨지지 않는다.
/// iOS 13 부터는 세기를 연속으로 줄 수 있어 Case 2 에는 충분하다.
/// 다만 한 번 울리고 끝이라 Case 3(연속 변조)에는 쓸 수 없다.
static id g_impact[3] = { nil, nil, nil };   // light / medium / heavy

static void ImpactFallback(float intensity)
{
    void (^fire)(void) = ^{
        // Case 2 는 최대 초당 12번까지 부른다. 매번 생성자를 새로 만들면
        // 할당이 쌓이고 prepare 가 더워지기 전에 호출돼 빠지는 펄스가 생긴다.
        // 세 단계를 한 번만 만들어 두고 재사용한다.
        int slot;
        UIImpactFeedbackStyle style;
        if      (intensity < 0.34f) { slot = 0; style = UIImpactFeedbackStyleLight;  }
        else if (intensity < 0.67f) { slot = 1; style = UIImpactFeedbackStyleMedium; }
        else                        { slot = 2; style = UIImpactFeedbackStyleHeavy;  }

        if (g_impact[slot] == nil) {
            g_impact[slot] = [[UIImpactFeedbackGenerator alloc] initWithStyle:style];
        }
        UIImpactFeedbackGenerator *gen = (UIImpactFeedbackGenerator *)g_impact[slot];

        if (@available(iOS 13.0, *)) {
            [gen impactOccurredWithIntensity:intensity];
        } else {
            [gen impactOccurred];
        }
        [gen prepare];          // 다음 호출을 위해 미리 더워지게 둔다
        g_impactOk++;
    };

    if ([NSThread isMainThread]) fire();
    else dispatch_async(dispatch_get_main_queue(), fire);
}

/// 마지막 수단. 세기도 길이도 고정이고 꽤 길게 울린다.
/// 실험에는 쓸 수 없고, 기기가 진동 자체는 할 수 있는지 가리는 용도다.
static void AudioFallback(void)
{
    AudioServicesPlaySystemSound(kSystemSoundID_Vibrate);
    g_audioOk++;
}

// ───────────────────────── Unity 가 부르는 함수 ─────────────────────────

extern "C" {

int ch_init(void)
{
    BOOL ok = EnsureRunning();
    if (!ok) {
        // CoreHaptics 가 안 되더라도 Case 2 는 UIImpactFeedback 으로 돌릴 수 있다.
        NSLog(@"[CardboardHaptics] CoreHaptics 사용 불가 — UIImpactFeedback 으로 대체한다");
    }
    return ok ? 1 : 0;
}

/// CoreHaptics(세기 자유 지정)를 쓸 수 있으면 1.
/// 0 이면 Case 3(세기 변조)는 이 기기에서 측정이 불가능하다.
int ch_supportsHaptics(void)
{
    return (g_hwSupports && g_running) ? 1 : 0;
}

/// 짧은 펄스 한 번. intensity / sharpness 는 0.0~1.0.
void ch_transient(float intensity, float sharpness)
{
    if (intensity < 0.0f) intensity = 0.0f;
    if (intensity > 1.0f) intensity = 1.0f;

    if (@available(iOS 13.0, *)) {
        if (EnsureRunning()) {
            NSError *error = nil;

            CHHapticEventParameter *pi =
                [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity
                                                             value:intensity];
            CHHapticEventParameter *ps =
                [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness
                                                             value:sharpness];
            CHHapticEvent *event =
                [[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticTransient
                                             parameters:@[pi, ps]
                                           relativeTime:0];

            CHHapticPattern *pattern = [[CHHapticPattern alloc] initWithEvents:@[event]
                                                                   parameters:@[]
                                                                        error:&error];
            if (pattern != nil && error == nil) {
                error = nil;
                id<CHHapticPatternPlayer> player =
                    [(CHHapticEngine *)g_engine createPlayerWithPattern:pattern error:&error];
                if (player != nil && error == nil) {
                    error = nil;
                    [player startAtTime:0 error:&error];
                    if (error == nil) {
                        g_coreOk++;
                        return;
                    }
                    SetErr([NSString stringWithFormat:@"play: %@", error.localizedDescription]);
                } else {
                    SetErr([NSString stringWithFormat:@"player: %@", error.localizedDescription]);
                }
            } else {
                SetErr([NSString stringWithFormat:@"pattern: %@", error.localizedDescription]);
            }
            g_coreFail++;
        }
    }

    // CoreHaptics 가 실패했다 — 그래도 뭔가는 울려야 원인을 가릴 수 있다
    ImpactFallback(intensity);
}

/// 세기를 실시간으로 바꿀 수 있는 연속 진동 시작. CoreHaptics 전용.
void ch_beginContinuous(float intensity, float sharpness)
{
    if (intensity < 0.0f) intensity = 0.0f;
    if (intensity > 1.0f) intensity = 1.0f;

    if (@available(iOS 13.0, *)) {
        if (!EnsureRunning()) return;
        if (g_continuous != nil) return;             // 이미 돌고 있음

        NSError *error = nil;

        CHHapticEventParameter *pi =
            [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity
                                                         value:intensity];
        CHHapticEventParameter *ps =
            [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness
                                                         value:sharpness];
        // 넉넉히 길게 잡고 루프로 돌린다. 시행이 끝나면 ch_stopContinuous 로 끊는다.
        CHHapticEvent *event =
            [[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticContinuous
                                         parameters:@[pi, ps]
                                       relativeTime:0
                                           duration:30.0];

        CHHapticPattern *pattern = [[CHHapticPattern alloc] initWithEvents:@[event]
                                                               parameters:@[]
                                                                    error:&error];
        if (pattern == nil || error != nil) {
            SetErr([NSString stringWithFormat:@"cont pattern: %@", error.localizedDescription]);
            return;
        }

        error = nil;
        id<CHHapticAdvancedPatternPlayer> player =
            [(CHHapticEngine *)g_engine createAdvancedPlayerWithPattern:pattern error:&error];
        if (player == nil || error != nil) {
            SetErr([NSString stringWithFormat:@"cont player: %@", error.localizedDescription]);
            return;
        }

        player.loopEnabled = YES;

        error = nil;
        [player startAtTime:0 error:&error];
        if (error != nil) {
            SetErr([NSString stringWithFormat:@"cont start: %@", error.localizedDescription]);
            return;
        }

        g_continuous = player;
        g_coreOk++;
    }
}

/// 연속 진동의 세기만 즉시 갱신 (0.0~1.0).
void ch_setContinuousIntensity(float intensity)
{
    if (intensity < 0.0f) intensity = 0.0f;
    if (intensity > 1.0f) intensity = 1.0f;

    if (@available(iOS 13.0, *)) {
        if (g_continuous == nil) {
            // 엔진이 중간에 멈춰 player 가 날아간 경우 — 다시 띄운다
            ch_beginContinuous(intensity, 0.5f);
            return;
        }
        NSError *error = nil;
        CHHapticDynamicParameter *p =
            [[CHHapticDynamicParameter alloc] initWithParameterID:CHHapticDynamicParameterIDHapticIntensityControl
                                                           value:intensity
                                                    relativeTime:0];
        [(id<CHHapticAdvancedPatternPlayer>)g_continuous sendParameters:@[p] atTime:0 error:&error];
        if (error != nil) {
            SetErr([NSString stringWithFormat:@"cont param: %@", error.localizedDescription]);
            g_continuous = nil;
        }
    }
}

void ch_stopContinuous(void)
{
    if (@available(iOS 13.0, *)) {
        if (g_continuous == nil) return;
        NSError *error = nil;
        [(id<CHHapticAdvancedPatternPlayer>)g_continuous stopAtTime:0 error:&error];
        g_continuous = nil;
    }
}

void ch_stopAll(void)
{
    ch_stopContinuous();
    if (@available(iOS 13.0, *)) {
        if (g_engine != nil) {
            [(CHHapticEngine *)g_engine stopWithCompletionHandler:nil];
            g_running = NO;
        }
    }
}

/// 세기 3단계 진동을 직접 호출한다 (CoreHaptics 를 건너뛴다).
/// 자가진단에서 두 경로를 나눠 확인하기 위해 쓴다.
void ch_impact(float intensity)
{
    ImpactFallback(intensity);
}

/// 세기 고정 진동을 강제로 한 번. 기기가 진동 자체는 되는지 가리는 용도.
void ch_systemVibrate(void)
{
    AudioFallback();
}

/// 진단 문자열. 맥이 없어 Xcode 콘솔을 못 보므로 이걸 HUD 에 띄운다.
const char* ch_diagnostics(void)
{
    snprintf(g_diag, sizeof(g_diag),
             "hw=%d run=%d cont=%d / core %d:%d impact=%d sys=%d / %s",
             g_hwSupports ? 1 : 0,
             g_running ? 1 : 0,
             g_continuous != nil ? 1 : 0,
             g_coreOk, g_coreFail, g_impactOk, g_audioOk,
             g_lastErr[0] ? g_lastErr : "(no error)");
    return g_diag;
}

} // extern "C"
