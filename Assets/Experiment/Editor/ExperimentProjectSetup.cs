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

    [MenuItem("Tools/Experiment/Setup/Create Profiles and Scenes", false, 90)]
    private static void CreateProfilesAndScenesFromMenu()
    {
        bool confirmed = EditorUtility.DisplayDialog(
            "Create Profiles and Scenes",
            "This regenerates the experiment scenes and resets generated profile settings. " +
            "Continue only when you intend to rebuild the project setup.",
            "Regenerate",
            "Cancel");
        if (confirmed) CreateProfilesAndScenes();
    }

    // Kept public for deterministic command-line setup without an editor dialog.
    public static void CreateProfilesAndScenes()
    {
        EnsureFolders(); ProfileCatalog catalog = BuildProfiles(); BuildExperimentScene(catalog); BuildMobileScene(catalog); BuildPcScene(catalog);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ExperimentScenePath, true), new EditorBuildSettingsScene(MobileTestScenePath, false) };
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); Debug.Log("[ExperimentSetup] Profiles and three separated scenes are ready.");
    }

    // Repairs scene component references without rebuilding profiles or resetting timing data.
    // Kept as a batch entry point rather than a normal Tools menu item.
    public static void RepairGeneratedSceneReferences()
    {
        ProfileCatalog catalog = AssetDatabase.LoadAssetAtPath<ProfileCatalog>(CatalogPath);
        ExperimentProtocol protocol = AssetDatabase.LoadAssetAtPath<ExperimentProtocol>(ProtocolPath);
        if (protocol != null && protocol.EnsureConditionPlans())
            EditorUtility.SetDirty(protocol);
        RepairScene(ExperimentScenePath, catalog, protocol, false, false);
        RepairScene(MobileTestScenePath, catalog, protocol, true, false);
        RepairScene(PcScenarioTestScenePath, catalog, protocol, false, true);
        AssetDatabase.SaveAssets();
        Debug.Log("[ExperimentSetup] Generated scene references repaired without changing profiles.");
    }

    private static ProfileCatalog BuildProfiles()
    {
        AudioClip appleClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/ScenarioSources/Apple/AppleCrunch.wav");
        AudioClip drillClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/ScenarioSources/DentalDrill/DentalTurbine.mp3");
        SoundProfile appleSound = GetOrCreate<SoundProfile>("Assets/Profiles/Sounds/Apple_SoundProfile.asset"); ConfigureSound(appleSound, "apple-sound", appleClip, "Existing Apple recording migrated here; replace when final audio is approved.");
        SoundProfile drillSound = GetOrCreate<SoundProfile>("Assets/Profiles/Sounds/DentalDrill_SoundProfile.asset"); ConfigureSound(drillSound, "dental-drill-sound", drillClip, "Existing turbine recording migrated here; replace when final audio is approved.");
        SoundProfile brushSound = GetOrCreate<SoundProfile>("Assets/Profiles/Sounds/Toothbrush_SoundProfile.asset"); ConfigureSound(brushSound, "toothbrush-sound", null, "Final toothbrush audio is pending; assign it here without changing ScenarioProfile.");
        ScenarioProfile apple = GetOrCreate<ScenarioProfile>("Assets/Profiles/Scenarios/Apple_ScenarioProfile.asset");
        ConfigureScenario(apple, "apple-scenario", ScenarioId.Apple, appleSound,
            AppleCrunchData.SegmentSeconds * AppleCrunchData.Segments, AppleBlocks());
        ScenarioProfile drill = GetOrCreate<ScenarioProfile>("Assets/Profiles/Scenarios/DentalDrill_ScenarioProfile.asset");
        ConfigureScenario(drill, "dental-drill-scenario", ScenarioId.DentalDrill, drillSound, 4.4f,
            Block("drill-approach", AnimationBlockKind.Approach, ScenarioAnimationAction.DrillApproach, 0, 1.2f),
            Block("drill-contact", AnimationBlockKind.Contact, ScenarioAnimationAction.DrillContact, 1.2f, 2.1f),
            Block("drill-retreat", AnimationBlockKind.Retreat, ScenarioAnimationAction.DrillRetreat, 3.3f, 1.1f));
        ScenarioProfile brush = GetOrCreate<ScenarioProfile>("Assets/Profiles/Scenarios/Toothbrush_ScenarioProfile.asset");
        ConfigureScenario(brush, "toothbrush-scenario", ScenarioId.Toothbrush, brushSound, 12f,
            Block("brush-approach", AnimationBlockKind.Approach, ScenarioAnimationAction.BrushApproach, 0, 1.2f),
            Block("brush-contact", AnimationBlockKind.Contact, ScenarioAnimationAction.BrushContact, 1.2f, 9.7f),
            Block("brush-retreat", AnimationBlockKind.Retreat, ScenarioAnimationAction.BrushRetreat, 10.9f, 1.1f));
        ExperimentProtocol protocol = GetOrCreate<ExperimentProtocol>(ProtocolPath); protocol.targetPlatform = RuntimePlatform.Android;
        protocol.conditions = new List<ExperimentCondition> { ExperimentCondition.Baseline, ExperimentCondition.ResearcherHaptic }; protocol.participantConditionEnabled = false;
        protocol.scenarios = new List<ProtocolScenario> { new ProtocolScenario { scenario = drill }, new ProtocolScenario { scenario = brush }, new ProtocolScenario { scenario = apple } };
        protocol.conditionPlans = new List<ExperimentConditionPlan>
        {
            ConditionPlan(ExperimentCondition.Baseline, true, drill, brush, apple),
            ConditionPlan(ExperimentCondition.ResearcherHaptic, true, drill, brush, apple),
            ConditionPlan(ExperimentCondition.ParticipantHaptic, false, drill, brush, apple)
        };
        ProfileCatalog catalog = GetOrCreate<ProfileCatalog>(CatalogPath); catalog.sounds = new List<SoundProfile> { appleSound, drillSound, brushSound }; catalog.haptics = new List<HapticProfile>(); catalog.scenarios = new List<ScenarioProfile> { drill, brush, apple }; catalog.protocols = new List<ExperimentProtocol> { protocol };
        MarkDirty(appleSound, drillSound, brushSound, apple, drill, brush, protocol, catalog); return catalog;
    }
    private static void ConfigureSound(SoundProfile p, string id, AudioClip clip, string note) { p.SetIdentity(id); p.clip = clip; p.placeholder = true; p.sourceNote = note; }
    private static ScenarioAnimationBlock Block(string id, AnimationBlockKind kind,
        ScenarioAnimationAction action, float start, float duration, int iteration = 0) => new ScenarioAnimationBlock
    {
        blockId = id,
        kind = kind,
        action = action,
        track = AnimationTrack.Primary,
        startSeconds = start,
        durationSeconds = duration,
        timeMode = BlockTimeMode.Stretch,
        playbackSpeed = 1f,
        iteration = iteration
    };

    private static ScenarioAnimationBlock[] AppleBlocks()
    {
        var result = new List<ScenarioAnimationBlock>();
        for (int i = 0; i < AppleCrunchData.Segments; i++)
        {
            float start = i * AppleCrunchData.SegmentSeconds;
            result.Add(Block($"apple-plate-{i + 1}", AnimationBlockKind.Setup,
                ScenarioAnimationAction.ApplePlateWait, start, 1.4f, i));
            start += 1.4f;
            result.Add(Block($"apple-approach-{i + 1}", AnimationBlockKind.Approach,
                ScenarioAnimationAction.AppleApproach, start, 1.2f, i));
            start += 1.2f;
            result.Add(Block($"apple-bite-{i + 1}", AnimationBlockKind.Contact,
                ScenarioAnimationAction.AppleBite, start, AppleCrunchData.HoldSeconds, i));
            start += AppleCrunchData.HoldSeconds;
            result.Add(Block($"apple-chew-{i + 1}", AnimationBlockKind.Contact,
                ScenarioAnimationAction.AppleChew, start, AppleCrunchData.ChewSeconds, i));
            start += AppleCrunchData.ChewSeconds;
            result.Add(Block($"apple-rest-{i + 1}", AnimationBlockKind.Finish,
                ScenarioAnimationAction.AppleRest, start, .9f, i));
        }
        return result.ToArray();
    }
    private static void ConfigureScenario(ScenarioProfile p, string id, ScenarioId scenario, SoundProfile sound, float duration, params ScenarioAnimationBlock[] blocks)
    { p.SetIdentity(id); p.scenario = scenario; p.sound = sound; p.animationDurationSeconds = duration; p.soundOffsetSeconds = p.hapticOffsetSeconds = 0; p.loop = false; p.animationBlocks = new List<ScenarioAnimationBlock>(blocks); }
    private static ExperimentConditionPlan ConditionPlan(ExperimentCondition condition, bool enabled,
        params ScenarioProfile[] profiles)
    {
        var plan = new ExperimentConditionPlan { condition = condition, enabled = enabled };
        foreach (ScenarioProfile profile in profiles)
            plan.scenarios.Add(new ConditionScenarioAssignment
            { scenario = profile.scenario, profile = profile, repetitions = 1 });
        return plan;
    }
    private static void BuildExperimentScene(ProfileCatalog catalog)
    { Scene scene = OpenTemplate(); StripLegacy(); GameObject runtime = CreateRuntime("Experiment Runtime", catalog.FindScenario(ScenarioId.Apple)); var session = runtime.AddComponent<ExperimentSessionController>(); session.protocol = AssetDatabase.LoadAssetAtPath<ExperimentProtocol>(ProtocolPath); session.player = runtime.GetComponent<ScenarioPlaybackController>(); session.playOnStart = false; var ui = runtime.AddComponent<ExperimentConditionSelectionUI>(); ui.session = session; EditorSceneManager.SaveScene(scene, ExperimentScenePath); }
    private static void BuildMobileScene(ProfileCatalog catalog)
    { Scene scene = OpenTemplate(); StripLegacy(); GameObject runtime = CreateRuntime("Mobile Test Runtime", catalog.FindScenario(ScenarioId.Apple)); var receiver = runtime.AddComponent<HapticLinkReceiver>(); var controller = runtime.AddComponent<MobileTestController>(); controller.catalog = catalog; controller.player = runtime.GetComponent<ScenarioPlaybackController>(); controller.hapticPlayer = runtime.GetComponent<LongHapticPlayer>(); controller.linkReceiver = receiver; EditorSceneManager.SaveScene(scene, MobileTestScenePath); }
    private static void BuildPcScene(ProfileCatalog catalog)
    { Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(PCScenarioCameraController)); camera.tag = "MainCamera"; camera.transform.position = new Vector3(0, 1.65f, 0); var light = new GameObject("Directional Light", typeof(Light)); light.GetComponent<Light>().type = LightType.Directional; light.transform.rotation = Quaternion.Euler(45, -30, 0); GameObject runtime = CreateRuntime("PC Scenario Test Runtime", catalog.FindScenario(ScenarioId.Apple)); var player = runtime.GetComponent<ScenarioPlaybackController>(); player.playOnStart = false; var preview = runtime.AddComponent<PCScenarioPreviewController>(); preview.player = player; preview.profile = player.profile; preview.autoStart = false; EditorSceneManager.SaveScene(scene, PcScenarioTestScenePath); }
    private static Scene OpenTemplate() => EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
    private static void StripLegacy() { foreach (ExperimentController c in Object.FindObjectsByType<ExperimentController>(FindObjectsInactive.Include)) Object.DestroyImmediate(c.gameObject); GameObject canvas = GameObject.Find("Canvas"); if (canvas != null) Object.DestroyImmediate(canvas); }
    private static GameObject CreateRuntime(string name, ScenarioProfile defaultScenario)
    { var runtime = new GameObject(name); runtime.AddComponent<AudioSource>().playOnAwake = false; runtime.AddComponent<LongHapticPlayer>(); var driver = runtime.AddComponent<MirrorScenarioAnimationDriver>(); driver.viewer = Camera.main != null ? Camera.main.transform : null; var player = runtime.AddComponent<ScenarioPlaybackController>(); player.profile = defaultScenario; player.animationDriver = driver; return runtime; }
    private static void RepairScene(string path, ProfileCatalog catalog,
        ExperimentProtocol protocol, bool mobileTest, bool pcTest)
    {
        Scene scene = SceneManager.GetSceneByPath(path);
        bool openedForRepair = !scene.IsValid() || !scene.isLoaded;
        if (openedForRepair) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

        try
        {
            ScenarioPlaybackController player = FindInScene<ScenarioPlaybackController>(scene);
            Camera camera = FindInScene<Camera>(scene);
            if (player == null || camera == null)
                throw new System.InvalidOperationException($"{path}: player or camera is missing.");

            GameObject runtime = player.gameObject;
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(runtime);
            MirrorScenarioAnimationDriver[] drivers =
                runtime.GetComponents<MirrorScenarioAnimationDriver>();
            MirrorScenarioAnimationDriver driver = drivers.Length > 0
                ? drivers[0]
                : runtime.AddComponent<MirrorScenarioAnimationDriver>();
            for (int i = 1; i < drivers.Length; i++)
                Object.DestroyImmediate(drivers[i]);
            driver.viewer = camera.transform;
            player.animationDriver = driver;
            if (player.profile == null && catalog != null)
                player.profile = catalog.FindScenario(ScenarioId.Apple);

            if (mobileTest)
            {
                LongHapticPlayer haptic = runtime.GetComponent<LongHapticPlayer>();
                if (haptic == null) haptic = runtime.AddComponent<LongHapticPlayer>();
                HapticLinkReceiver receiver = runtime.GetComponent<HapticLinkReceiver>();
                if (receiver == null) receiver = runtime.AddComponent<HapticLinkReceiver>();
                MobileTestController controller = runtime.GetComponent<MobileTestController>();
                if (controller == null) controller = runtime.AddComponent<MobileTestController>();
                controller.catalog = catalog;
                controller.player = player;
                controller.hapticPlayer = haptic;
                controller.linkReceiver = receiver;
                EditorUtility.SetDirty(controller);
                EditorUtility.SetDirty(receiver);
            }
            else if (pcTest && camera.GetComponent<PCScenarioCameraController>() == null)
            {
                camera.gameObject.AddComponent<PCScenarioCameraController>();
            }
            else if (!pcTest)
            {
                ExperimentSessionController session = runtime.GetComponent<ExperimentSessionController>();
                if (session == null) session = runtime.AddComponent<ExperimentSessionController>();
                session.protocol = protocol;
                session.player = player;
                session.playOnStart = false;
                ExperimentConditionSelectionUI ui = runtime.GetComponent<ExperimentConditionSelectionUI>();
                if (ui == null) ui = runtime.AddComponent<ExperimentConditionSelectionUI>();
                ui.session = session;
                EditorUtility.SetDirty(session);
                EditorUtility.SetDirty(ui);
            }

            if (pcTest)
            {
                player.playOnStart = false;
                PCScenarioPreviewController preview = runtime.GetComponent<PCScenarioPreviewController>();
                if (preview == null) preview = runtime.AddComponent<PCScenarioPreviewController>();
                preview.player = player;
                preview.autoStart = false;
                EditorUtility.SetDirty(preview);
            }

            EditorUtility.SetDirty(driver);
            EditorUtility.SetDirty(player);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, path);
        }
        finally
        {
            if (openedForRepair && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }
    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T component = root.GetComponentInChildren<T>(true);
            if (component != null) return component;
        }
        return null;
    }
    private static T GetOrCreate<T>(string path) where T : ScriptableObject { T asset = AssetDatabase.LoadAssetAtPath<T>(path); if (asset != null) return asset; asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); return asset; }
    private static void MarkDirty(params Object[] assets) { foreach (Object asset in assets) if (asset != null) EditorUtility.SetDirty(asset); }
    private static void EnsureFolders() { string[] folders = { "Assets/Profiles", "Assets/Profiles/Sounds", "Assets/Profiles/Haptics", "Assets/Profiles/Haptics/Manual", "Assets/Profiles/Haptics/SoundBased", "Assets/Profiles/Haptics/Researcher", "Assets/Profiles/Scenarios", "Assets/Profiles/Scenarios/Variants", "Assets/Profiles/Scenarios/Variants/Apple", "Assets/Profiles/Scenarios/Variants/DentalDrill", "Assets/Profiles/Scenarios/Variants/Toothbrush", "Assets/Profiles/Experiments", "Assets/Audio/ScenarioSources/Toothbrush", "Assets/Audio/Preview", "Assets/Audio/Calibration" }; foreach (string folder in folders) ManualHapticGeneratorWindow.EnsureFolder(folder); }
}
