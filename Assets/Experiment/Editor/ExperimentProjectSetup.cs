using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ExperimentProjectSetup
{
    public const string ExperimentScenePath = "Assets/Scenes/ExperimentScene.unity";
    public const string MobileTestScenePath = "Assets/Scenes/MobileTestScene.unity";
    public const string PcScenarioTestScenePath = "Assets/Scenes/PCScenarioTestScene.unity";
    public const string CatalogPath = "Assets/Profiles/ProfileCatalog.asset";
    public const string ProtocolPath = "Assets/Profiles/Experiments/AndroidExperimentProtocol.asset";

    [MenuItem("Tools/Experiment/Create Profiles and Scenes")]
    public static void CreateProfilesAndScenes()
    {
        EnsureFolders(); ProfileCatalog catalog = BuildProfiles(); BuildExperimentScene(catalog); BuildMobileScene(catalog); BuildPcScene(catalog);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ExperimentScenePath, true), new EditorBuildSettingsScene(MobileTestScenePath, false) };
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); Debug.Log("[ExperimentSetup] Profiles and three separated scenes are ready.");
    }

    private static ProfileCatalog BuildProfiles()
    {
        AudioClip appleClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/ScenarioSources/Apple/AppleCrunch.wav");
        AudioClip drillClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/ScenarioSources/DentalDrill/DentalTurbine.mp3");
        SoundProfile appleSound = GetOrCreate<SoundProfile>("Assets/Profiles/Sounds/Apple_SoundProfile.asset"); ConfigureSound(appleSound, "apple-sound", appleClip, "Existing Apple recording migrated here; replace when final audio is approved.");
        SoundProfile drillSound = GetOrCreate<SoundProfile>("Assets/Profiles/Sounds/DentalDrill_SoundProfile.asset"); ConfigureSound(drillSound, "dental-drill-sound", drillClip, "Existing turbine recording migrated here; replace when final audio is approved.");
        SoundProfile brushSound = GetOrCreate<SoundProfile>("Assets/Profiles/Sounds/Toothbrush_SoundProfile.asset"); ConfigureSound(brushSound, "toothbrush-sound", null, "Final toothbrush audio is pending; assign it here without changing ScenarioProfile.");
        ScenarioProfile apple = GetOrCreate<ScenarioProfile>("Assets/Profiles/Scenarios/Apple_ScenarioProfile.asset");
        ConfigureScenario(apple, "apple-scenario", ScenarioId.Apple, appleSound, 47.1f,
            Block("setup", AnimationBlockKind.Setup, 0, 1.4f), Block("approach", AnimationBlockKind.Approach, 1.4f, 1.2f), Block("bite-and-chew", AnimationBlockKind.Contact, 2.6f, 43.6f), Block("finish", AnimationBlockKind.Finish, 46.2f, .9f));
        ScenarioProfile drill = GetOrCreate<ScenarioProfile>("Assets/Profiles/Scenarios/DentalDrill_ScenarioProfile.asset");
        ConfigureScenario(drill, "dental-drill-scenario", ScenarioId.DentalDrill, drillSound, 4.4f,
            Block("approach", AnimationBlockKind.Approach, 0, 1.2f), Block("contact", AnimationBlockKind.Contact, 1.2f, 2.1f), Block("retreat", AnimationBlockKind.Retreat, 3.3f, 1.1f));
        ScenarioProfile brush = GetOrCreate<ScenarioProfile>("Assets/Profiles/Scenarios/Toothbrush_ScenarioProfile.asset");
        ConfigureScenario(brush, "toothbrush-scenario", ScenarioId.Toothbrush, brushSound, 12f,
            Block("approach", AnimationBlockKind.Approach, 0, 1.2f), Block("brushing", AnimationBlockKind.Contact, 1.2f, 9.7f), Block("retreat", AnimationBlockKind.Retreat, 10.9f, 1.1f));
        ExperimentProtocol protocol = GetOrCreate<ExperimentProtocol>(ProtocolPath); protocol.targetPlatform = RuntimePlatform.Android;
        protocol.conditions = new List<ExperimentCondition> { ExperimentCondition.Baseline, ExperimentCondition.ResearcherHaptic }; protocol.participantConditionEnabled = false;
        protocol.scenarios = new List<ProtocolScenario> { new ProtocolScenario { scenario = drill }, new ProtocolScenario { scenario = brush }, new ProtocolScenario { scenario = apple } };
        ProfileCatalog catalog = GetOrCreate<ProfileCatalog>(CatalogPath); catalog.sounds = new List<SoundProfile> { appleSound, drillSound, brushSound }; catalog.haptics = new List<HapticProfile>(); catalog.scenarios = new List<ScenarioProfile> { drill, brush, apple }; catalog.protocols = new List<ExperimentProtocol> { protocol };
        MarkDirty(appleSound, drillSound, brushSound, apple, drill, brush, protocol, catalog); return catalog;
    }
    private static void ConfigureSound(SoundProfile p, string id, AudioClip clip, string note) { p.SetIdentity(id); p.clip = clip; p.placeholder = true; p.sourceNote = note; }
    private static ScenarioAnimationBlock Block(string id, AnimationBlockKind kind, float start, float duration) => new ScenarioAnimationBlock { blockId = id, kind = kind, startSeconds = start, durationSeconds = duration };
    private static void ConfigureScenario(ScenarioProfile p, string id, ScenarioId scenario, SoundProfile sound, float duration, params ScenarioAnimationBlock[] blocks)
    { p.SetIdentity(id); p.scenario = scenario; p.sound = sound; p.animationDurationSeconds = duration; p.soundOffsetSeconds = p.hapticOffsetSeconds = 0; p.loop = false; p.animationBlocks = new List<ScenarioAnimationBlock>(blocks); }
    private static void BuildExperimentScene(ProfileCatalog catalog)
    { Scene scene = OpenTemplate(); StripLegacy(); GameObject runtime = CreateRuntime("Experiment Runtime", catalog.FindScenario(ScenarioId.Apple)); var session = runtime.AddComponent<ExperimentSessionController>(); session.protocol = AssetDatabase.LoadAssetAtPath<ExperimentProtocol>(ProtocolPath); session.player = runtime.GetComponent<ScenarioPlaybackController>(); EditorSceneManager.SaveScene(scene, ExperimentScenePath); }
    private static void BuildMobileScene(ProfileCatalog catalog)
    { Scene scene = OpenTemplate(); StripLegacy(); GameObject runtime = CreateRuntime("Mobile Test Runtime", catalog.FindScenario(ScenarioId.Apple)); var receiver = runtime.AddComponent<HapticLinkReceiver>(); var controller = runtime.AddComponent<MobileTestController>(); controller.catalog = catalog; controller.player = runtime.GetComponent<ScenarioPlaybackController>(); controller.hapticPlayer = runtime.GetComponent<LongHapticPlayer>(); controller.linkReceiver = receiver; EditorSceneManager.SaveScene(scene, MobileTestScenePath); }
    private static void BuildPcScene(ProfileCatalog catalog)
    { Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)); camera.tag = "MainCamera"; camera.transform.position = new Vector3(0, 1.65f, 0); var light = new GameObject("Directional Light", typeof(Light)); light.GetComponent<Light>().type = LightType.Directional; light.transform.rotation = Quaternion.Euler(45, -30, 0); GameObject runtime = CreateRuntime("PC Scenario Test Runtime", catalog.FindScenario(ScenarioId.Apple)); runtime.GetComponent<ScenarioPlaybackController>().playOnStart = true; EditorSceneManager.SaveScene(scene, PcScenarioTestScenePath); }
    private static Scene OpenTemplate() => EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
    private static void StripLegacy() { foreach (ExperimentController c in Object.FindObjectsByType<ExperimentController>(FindObjectsInactive.Include)) Object.DestroyImmediate(c.gameObject); GameObject canvas = GameObject.Find("Canvas"); if (canvas != null) Object.DestroyImmediate(canvas); }
    private static GameObject CreateRuntime(string name, ScenarioProfile defaultScenario)
    { var runtime = new GameObject(name); runtime.AddComponent<AudioSource>().playOnAwake = false; runtime.AddComponent<LongHapticPlayer>(); var driver = runtime.AddComponent<MirrorScenarioAnimationDriver>(); driver.viewer = Camera.main != null ? Camera.main.transform : null; var player = runtime.AddComponent<ScenarioPlaybackController>(); player.profile = defaultScenario; player.animationDriver = driver; return runtime; }
    private static T GetOrCreate<T>(string path) where T : ScriptableObject { T asset = AssetDatabase.LoadAssetAtPath<T>(path); if (asset != null) return asset; asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); return asset; }
    private static void MarkDirty(params Object[] assets) { foreach (Object asset in assets) if (asset != null) EditorUtility.SetDirty(asset); }
    private static void EnsureFolders() { string[] folders = { "Assets/Profiles", "Assets/Profiles/Sounds", "Assets/Profiles/Haptics", "Assets/Profiles/Haptics/Manual", "Assets/Profiles/Haptics/SoundBased", "Assets/Profiles/Haptics/Researcher", "Assets/Profiles/Scenarios", "Assets/Profiles/Experiments", "Assets/Audio/ScenarioSources/Toothbrush", "Assets/Audio/Preview", "Assets/Audio/Calibration" }; foreach (string folder in folders) ManualHapticGeneratorWindow.EnsureFolder(folder); }
}
