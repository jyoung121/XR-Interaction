using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>Real player/rig checks in Play Mode; no source profiles or experiment assignments are changed.</summary>
public static class ActiveSceneContinuityVerification
{
    public sealed class WorldSnapshot
    {
        private readonly MirrorRig rig;
        private readonly Vector3 position;
        private readonly Quaternion rotation;
        private readonly Light[] lights;
        private readonly Color sky, equator, ground;
        private readonly UnityEngine.Rendering.AmbientMode ambient;
        private readonly Material skybox;
        private readonly float reflection;

        public WorldSnapshot(MirrorScenarioAnimationDriver driver)
        {
            rig = Rig(driver);
            Require(rig != null, "Prepared world is missing");
            position = rig.transform.position; rotation = rig.transform.rotation;
            lights = rig.GetComponentsInChildren<Light>(true);
            sky = RenderSettings.ambientSkyColor; equator = RenderSettings.ambientEquatorColor;
            ground = RenderSettings.ambientGroundColor; ambient = RenderSettings.ambientMode;
            skybox = RenderSettings.skybox; reflection = RenderSettings.reflectionIntensity;
        }
        public void AssertStable(MirrorScenarioAnimationDriver driver)
        {
            Require(Rig(driver) == rig && rig.gameObject.activeInHierarchy, "ON/OFF replaced or hid the prepared world");
            Require(Vector3.Distance(position, rig.transform.position) < .00001f &&
                Quaternion.Angle(rotation, rig.transform.rotation) < .001f, "ON/OFF reanchored the world");
            Require(lights.SequenceEqual(rig.GetComponentsInChildren<Light>(true)), "ON/OFF replaced lights");
            Require(RenderSettings.ambientMode == ambient && RenderSettings.skybox == skybox &&
                Mathf.Abs(RenderSettings.reflectionIntensity - reflection) < .00001f &&
                Same(RenderSettings.ambientSkyColor, sky) && Same(RenderSettings.ambientEquatorColor, equator) &&
                Same(RenderSettings.ambientGroundColor, ground), "ON/OFF changed environmental lighting");
        }
    }

    public static void Verify()
    {
        Require(Application.isPlaying, "Continuity checks must run in Play Mode");
        var viewer = new GameObject("ContinuityViewer").transform;
        var beforeMode = RenderSettings.ambientMode;
        Color beforeSky = RenderSettings.ambientSkyColor, beforeEquator = RenderSettings.ambientEquatorColor,
            beforeGround = RenderSettings.ambientGroundColor;
        try
        {
            foreach (var settings in ResearchExperimentSetup.Protocol.research.scenarios)
                VerifyScenario(settings, viewer);
            Require(RenderSettings.ambientMode == beforeMode && Same(RenderSettings.ambientSkyColor, beforeSky) &&
                Same(RenderSettings.ambientEquatorColor, beforeEquator) && Same(RenderSettings.ambientGroundColor, beforeGround),
                "Trial disposal did not restore all ambient colors");
            Debug.Log("[ActiveContinuity] PASS: scheduled cancellation, fixed world/lights after head turns, contact/approach retreat continuity, natural completion, Apple chew completion and full ambient restoration.");
        }
        finally { UnityEngine.Object.DestroyImmediate(viewer.gameObject); }
    }

    private static void VerifyScenario(ResearchScenarioSettings settings, Transform viewer)
    {
        var owner = new GameObject("Continuity " + settings.scenario.scenario);
        owner.AddComponent<AudioSource>(); owner.AddComponent<LongHapticPlayer>();
        var driver = owner.AddComponent<MirrorScenarioAnimationDriver>(); driver.viewer = viewer;
        var player = owner.AddComponent<ScenarioPlaybackController>(); player.animationDriver = driver;
        try
        {
            foreach (var block in settings.scenario.animationBlocks)
                Require(ScenarioAnimationTimeline.TrySample(settings.scenario, block.startSeconds, out var sample) &&
                    ReferenceEquals(sample.block, block), "Block start resolves to a preceding rounded-up block: " + block.blockId);
            Require(player.PrepareResearch(settings.scenario, ExperimentCondition.ResearcherHaptic, settings.soundBased), "Prepare failed");
            var snapshot = new WorldSnapshot(driver);
            bool cancelled = false;
            player.ResearchEvent += (kind, value) => { if (kind == "start_cancelled") cancelled = true; };
            Require(player.BeginResearch(settings.scenario, settings.soundBased), "Start failed");
            snapshot.AssertStable(driver);
            Require(player.ScenarioSeconds < 0 && player.FinishResearch(), "Could not cancel pending start");
            Require(cancelled && !player.IsPlaying && !owner.GetComponent<LongHapticPlayer>().IsPlaying &&
                !owner.GetComponent<AudioSource>().isPlaying, "Pending media was not cancelled");
            snapshot.AssertStable(driver);

            viewer.position += new Vector3(.01f, 0, 0);
            viewer.rotation *= Quaternion.Euler(0, 25, 0);
            Require(player.PrepareResearch(settings.scenario, ExperimentCondition.ResearcherHaptic, settings.soundBased), "Reprepare failed");
            snapshot.AssertStable(driver);
            if (settings.scenario.scenario == ScenarioId.Toothbrush)
            {
                VerifyRetreat(player, driver, settings, .25f, snapshot); // interrupted approach
                VerifyRetreat(player, driver, settings, 2.5f, snapshot); // contact depth + oscillation
                var retreat = settings.scenario.animationBlocks.First(b => b.kind == AnimationBlockKind.Retreat);
                var tool = Get<GameObject>(Rig(driver), "toothbrush").transform;
                driver.EvaluateTimeline(settings.scenario, 2.5f);
                driver.EvaluateTimeline(settings.scenario, retreat.startSeconds + retreat.durationSeconds * .5f);
                Vector3 expected = tool.localPosition; float expectedMouth = Rig(driver).MouthOpen;
                driver.EvaluateTimeline(settings.scenario, .1f);
                driver.EvaluateTimeline(settings.scenario, retreat.startSeconds + retreat.durationSeconds * .5f);
                Require(Vector3.Distance(expected, tool.localPosition) < .000001f &&
                    Mathf.Abs(expectedMouth - Rig(driver).MouthOpen) < .000001f, "Direct PC timeline sampling depends on seek history");
            }
            else
            {
                Require(player.BeginResearch(settings.scenario, settings.soundBased), "Apple start failed");
                var chew = settings.scenario.animationBlocks.First(b => b.action == ScenarioAnimationAction.AppleChew);
                float at = chew.startSeconds + chew.durationSeconds * .5f;
                Set(player, "clockStartDsp", AudioSettings.dspTime - at); Tick(player);
                Require(player.FinishResearch() && player.IsPlaying && player.State != ScenarioPlaybackState.Finishing,
                    "Apple OFF interrupted chewing");
                snapshot.AssertStable(driver);
                Set(player, "clockStartDsp", AudioSettings.dspTime - ResearchTimelineRules.AppleFinishSeconds(settings.scenario, at) - .001);
                Tick(player);
                Require(!player.IsPlaying, "Apple did not finish after its current rest");
                snapshot.AssertStable(driver);
            }
            Require(player.PrepareResearch(settings.scenario, ExperimentCondition.ResearcherHaptic, settings.soundBased), "Natural-end prepare failed");
            Require(player.BeginResearch(settings.scenario, settings.soundBased), "Natural-end start failed");
            Set(player, "clockStartDsp", AudioSettings.dspTime - settings.scenario.animationDurationSeconds - .001);
            Tick(player);
            Require(!player.IsPlaying, "Natural completion failed");
            snapshot.AssertStable(driver);
        }
        finally { player.Stop(); UnityEngine.Object.DestroyImmediate(owner); }
    }

    private static void VerifyRetreat(ScenarioPlaybackController player, MirrorScenarioAnimationDriver driver,
        ResearchScenarioSettings settings, float at, WorldSnapshot snapshot)
    {
        Require(player.PrepareResearch(settings.scenario, ExperimentCondition.ResearcherHaptic, settings.soundBased), "Retreat prepare failed");
        Require(player.BeginResearch(settings.scenario, settings.soundBased), "Retreat start failed");
        Set(player, "clockStartDsp", AudioSettings.dspTime - at); Tick(player);
        var rig = Rig(driver); var tool = Get<GameObject>(rig, "toothbrush").transform;
        Vector3 position = tool.localPosition; Quaternion rotation = tool.localRotation;
        float mouth = rig.MouthOpen;
        Require(player.FinishResearch(), "Retreat request failed");
        Require(Vector3.Distance(position, tool.localPosition) < .000001f && Quaternion.Angle(rotation, tool.localRotation) < .05f &&
            Mathf.Abs(mouth - rig.MouthOpen) < .000001f, "OFF snapped the tool or mouth to another start pose: at=" + at +
            "; position_delta=" + Vector3.Distance(position, tool.localPosition).ToString("R") +
            "; rotation_delta=" + Quaternion.Angle(rotation, tool.localRotation).ToString("R") +
            "; mouth_delta=" + Mathf.Abs(mouth - rig.MouthOpen).ToString("R"));
        float duration = Get<float>(player, "finishTransitionSeconds");
        Set(player, "finishStartDsp", AudioSettings.dspTime - duration * .5); Tick(player);
        Require(rig.MouthOpen <= mouth + .0001f, "Retreat reopened the mouth");
        snapshot.AssertStable(driver);
        Set(player, "finishStartDsp", AudioSettings.dspTime - duration - .001); Tick(player);
        Require(!player.IsPlaying, "Retreat failed to complete");
        Vector3 end = tool.localPosition;
        Require(player.PrepareResearch(settings.scenario, ExperimentCondition.ResearcherHaptic, settings.soundBased), "Idle prepare failed");
        Require(Vector3.Distance(end, tool.localPosition) < .0001f && rig.MouthOpen < .0001f, "Retreat-to-idle snapped");
        snapshot.AssertStable(driver);
    }

    private static bool Same(Color a, Color b) => (new Vector4(a.r - b.r, a.g - b.g, a.b - b.b, a.a - b.a)).sqrMagnitude < .0000001f;
    private static MirrorRig Rig(MirrorScenarioAnimationDriver driver) => Get<MirrorRig>(driver, "rig");
    private static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(owner);
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(owner, value);
    private static void Tick(ScenarioPlaybackController player) => typeof(ScenarioPlaybackController).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(player, null);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
