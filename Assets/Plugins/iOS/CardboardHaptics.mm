// CardboardHaptics.mm
// iOS CoreHaptics 래퍼 — Unity 에서 C 함수로 호출한다.
//
// Android 의 VibrationEffect 와 맞추기 위해 두 가지를 제공한다.
//   1) 짧은 펄스 (Case 2 — 주기 변조)        -> ch_transient
//   2) 세기를 실시간으로 바꾸는 연속 진동 (Case 3) -> ch_beginContinuous / ch_setContinuousIntensity
//
// 요구 사항: iOS 13.0 이상, Taptic Engine 탑재 기기(iPhone 8 이상).
// CoreHaptics 를 못 쓰는 기기에서는 AudioServices 의 고정 진동으로 떨어지며,
// 그 경우 ch_supportsHaptics() 가 0 을 돌려준다(= 세기 조절 불가).

#import <Foundation/Foundation.h>
#import <CoreHaptics/CoreHaptics.h>
#import <AudioToolbox/AudioToolbox.h>

static id   g_engine      = nil;   // CHHapticEngine *
static id   g_continuous  = nil;   // id<CHHapticAdvancedPatternPlayer>
static BOOL g_supported   = NO;
static BOOL g_initialized = NO;

static BOOL EnsureEngine(void)
{
    if (@available(iOS 13.0, *)) {
        if (!CHHapticEngine.capabilitiesForHardware.supportsHaptics) {
            g_supported = NO;
            return NO;
        }
        if (g_engine == nil) {
            NSError *error = nil;
            CHHapticEngine *engine = [[CHHapticEngine alloc] initAndReturnError:&error];
            if (engine == nil || error != nil) {
                NSLog(@"[CardboardHaptics] engine 생성 실패: %@", error);
                g_supported = NO;
                return NO;
            }
            engine.playsHapticsOnly = YES;

            __weak CHHapticEngine *weakEngine = engine;
            engine.resetHandler = ^{
                NSError *e = nil;
                [weakEngine startAndReturnError:&e];
                if (e) NSLog(@"[CardboardHaptics] reset 후 재시작 실패: %@", e);
            };
            engine.stoppedHandler = ^(CHHapticEngineStoppedReason reason) {
                NSLog(@"[CardboardHaptics] engine 정지: %ld", (long)reason);
            };

            [engine startAndReturnError:&error];
            if (error != nil) {
                NSLog(@"[CardboardHaptics] engine 시작 실패: %@", error);
                g_supported = NO;
                return NO;
            }
            g_engine = engine;
        }
        g_supported = YES;
        return YES;
    }
    g_supported = NO;
    return NO;
}

extern "C" {

/// CoreHaptics(세기 조절 포함)를 쓸 수 있으면 1. Android 의 hasAmplitudeControl 과 같은 의미.
int ch_supportsHaptics(void)
{
    if (!g_initialized) { EnsureEngine(); g_initialized = YES; }
    return g_supported ? 1 : 0;
}

int ch_init(void)
{
    g_initialized = YES;
    return EnsureEngine() ? 1 : 0;
}

/// 짧은 펄스 한 번. intensity/sharpness 는 0.0~1.0.
void ch_transient(float intensity, float sharpness)
{
    if (@available(iOS 13.0, *)) {
        if (!EnsureEngine()) {
            AudioServicesPlaySystemSound(kSystemSoundID_Vibrate);   // 고정 진동 폴백
            return;
        }
        NSError *error = nil;
        CHHapticEventParameter *i =
            [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity
                                                          value:intensity];
        CHHapticEventParameter *s =
            [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness
                                                          value:sharpness];
        CHHapticEvent *event =
            [[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticTransient
                                          parameters:@[i, s]
                                        relativeTime:0];
        CHHapticPattern *pattern = [[CHHapticPattern alloc] initWithEvents:@[event]
                                                                parameters:@[]
                                                                     error:&error];
        if (error) { NSLog(@"[CardboardHaptics] transient pattern 실패: %@", error); return; }

        id<CHHapticPatternPlayer> player = [(CHHapticEngine *)g_engine createPlayerWithPattern:pattern error:&error];
        if (error) { NSLog(@"[CardboardHaptics] transient player 실패: %@", error); return; }
        [player startAtTime:0 error:&error];
    } else {
        AudioServicesPlaySystemSound(kSystemSoundID_Vibrate);
    }
}

/// 세기를 실시간으로 바꿀 수 있는 연속 진동 시작.
void ch_beginContinuous(float intensity, float sharpness)
{
    if (@available(iOS 13.0, *)) {
        if (!EnsureEngine()) return;
        if (g_continuous != nil) return;             // 이미 돌고 있음

        NSError *error = nil;
        CHHapticEventParameter *i =
            [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity
                                                          value:intensity];
        CHHapticEventParameter *s =
            [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness
                                                          value:sharpness];
        // 넉넉히 길게 잡고 루프로 돌린다. 시행이 끝나면 ch_stopContinuous 로 끊는다.
        CHHapticEvent *event =
            [[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticContinuous
                                          parameters:@[i, s]
                                        relativeTime:0
                                            duration:30.0];
        CHHapticPattern *pattern = [[CHHapticPattern alloc] initWithEvents:@[event]
                                                                parameters:@[]
                                                                     error:&error];
        if (error) { NSLog(@"[CardboardHaptics] continuous pattern 실패: %@", error); return; }

        id<CHHapticAdvancedPatternPlayer> player =
            [(CHHapticEngine *)g_engine createAdvancedPlayerWithPattern:pattern error:&error];
        if (error || player == nil) { NSLog(@"[CardboardHaptics] continuous player 실패: %@", error); return; }

        player.loopEnabled = YES;
        [player startAtTime:0 error:&error];
        if (error) { NSLog(@"[CardboardHaptics] continuous 시작 실패: %@", error); return; }
        g_continuous = player;
    }
}

/// 연속 진동의 세기만 즉시 갱신 (0.0~1.0).
void ch_setContinuousIntensity(float intensity)
{
    if (@available(iOS 13.0, *)) {
        if (g_continuous == nil) return;
        NSError *error = nil;
        CHHapticDynamicParameter *p =
            [[CHHapticDynamicParameter alloc] initWithParameterID:CHHapticDynamicParameterIDHapticIntensityControl
                                                            value:intensity
                                                     relativeTime:0];
        [(id<CHHapticAdvancedPatternPlayer>)g_continuous sendParameters:@[p] atTime:0 error:&error];
        if (error) NSLog(@"[CardboardHaptics] 세기 갱신 실패: %@", error);
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
        if (g_engine != nil) [(CHHapticEngine *)g_engine stopWithCompletionHandler:nil];
        g_engine = nil;
    }
}

} // extern "C"
