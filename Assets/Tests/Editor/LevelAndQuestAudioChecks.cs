using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Run in an isolated project: -batchmode -executeMethod LevelAndQuestAudioChecks.RunBatch
public static class LevelAndQuestAudioChecks
{
    const string Pending = "LevelAndQuestAudioChecks.Pending";
    static int checks;

    [InitializeOnLoadMethod]
    static void Register()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false)) Run();
        };
    }

    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch-mode project.");
        try
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/combat.wav");
            foreach (var name in new[] { "KOTHTest", "EscortTest", "EliminationTest", "JhMapScene", "ZooScene", "EnemyBehaviorPlaytest", "JhCombatUIRevampScene", "MainMenuScene", "PreMissionScene" })
            {
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity");
                int count = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<AudioSource>(true))
                    .Count(s => s.clip == clip && s.loop && s.playOnAwake);
                bool menu = name == "MainMenuScene" || name == "PreMissionScene";
                Check(count == (menu ? 0 : 1), name + (menu ? " has no gameplay music" : " has exactly one automatic gameplay music source"));
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("LEVEL AUDIO FAIL: " + message);
        checks++; Debug.Log("LEVEL AUDIO PASS: " + message);
    }

    static async Task Wait(float seconds)
    {
        double until = Time.realtimeSinceStartupAsDouble + seconds;
        while (Time.realtimeSinceStartupAsDouble < until) await Task.Delay(10);
    }

    static async void Run()
    {
        SessionState.SetBool(Pending, false);
        try
        {
            new GameObject("Audio Check Listener", typeof(AudioListener));
            var levelScene = SceneManager.CreateScene("Level Music Playback");
            SceneManager.SetActiveScene(levelScene);
            var music = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Audio/LevelMusic.prefab")).GetComponent<AudioSource>();
            Check(music.clip.name == "combat" && music.isPlaying && music.loop && music.playOnAwake,
                "Level music starts automatically without waiting for enemies to aggro");
            Check(music.volume == 1f && music.pitch == 1f && music.spatialBlend == 0f && music.outputAudioMixerGroup.name == "Music",
                "Level music uses 2D playback at volume 1 and pitch 1 through the Music bus");
            // A streaming source can report isPlaying before its first DSP buffer is ready.
            double audioDeadline = Time.realtimeSinceStartupAsDouble + 4;
            while (music.timeSamples == 0 && Time.realtimeSinceStartupAsDouble < audioDeadline) await Task.Delay(10);
            Check(music.timeSamples > 0, "Streaming level music advances actual audio samples");
            int before = music.timeSamples; Time.timeScale = 0f;
            await Wait(.3f);
            Check(music.isPlaying && music.timeSamples != before, "Level music continues through paused gameplay and result overlays");
            music.time = music.clip.length - .1f;
            await Wait(.3f);
            Check(music.isPlaying && music.time < 1f, "Gameplay music actually loops at the end of the clip");

            var destination = SceneManager.CreateScene("Quest Audio Playback");
            SceneManager.SetActiveScene(destination);
            var unload = SceneManager.UnloadSceneAsync(levelScene);
            while (!unload.isDone) await Task.Delay(10);
            Check(music == null, "Leaving the level destroys its old music source");

            var slab = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/PreMission/QuestDescSlabP.prefab")).GetComponent<PreMissionQuestDetailsSlab>();
            slab.Bind(null, null);
            var fields = new SerializedObject(slab);
            var audio = (AudioSource)fields.FindProperty("appearanceAudio").objectReferenceValue;
            var sound = (AudioClip)fields.FindProperty("appearanceSound").objectReferenceValue;
            Check(sound.name == "start_quest_click" && audio != null && !audio.isPlaying,
                "The briefing prefab assigns the quest-details sound and stays silent while hidden");
            Check(!audio.playOnAwake && !audio.loop && audio.spatialBlend == 0f && audio.volume == 1f && audio.pitch == 1f &&
                audio.outputAudioMixerGroup.name == "SFX" && fields.FindProperty("appearanceVolume").floatValue == 1f,
                "The briefing arrival uses one 2D SFX voice with editable unit gain");

            var enter = slab.Enter(); enter.MoveNext();
            Check(slab.IsShown && audio.isPlaying && slab.Animator.GetCurrentAnimatorStateInfo(0).IsName("questDescSlabAnimFall"),
                "The sound begins with the details slab's falling animation, even at timeScale zero");
            audio.Stop();
            var duplicate = slab.Enter(); duplicate.MoveNext();
            Check(!audio.isPlaying, "A repeated entry while the slab is shown does not double-play its sound");
            (enter as IDisposable)?.Dispose(); (duplicate as IDisposable)?.Dispose();
            slab.Settle(true);
            slab.SetContent("Test Quest", "SIDE QUEST / KOTH", "Description", "Enemies spawned: Fodder", "Reward: 100");
            slab.RefreshPlacement();
            Check(!audio.isPlaying && slab.TitleText.text == "Test Quest", "Content refresh and settling/recovery stay silent and preserve the text binding");
            slab.Hide();
            slab.Settle(true);
            Check(!audio.isPlaying, "Restoring details from torso selection does not replay a drop sound");
            slab.Hide();
            var reopen = slab.Enter(); reopen.MoveNext();
            Check(audio.isPlaying, "Declining and selecting a quest again plays a fresh arrival sound");
            (reopen as IDisposable)?.Dispose();
            slab.enabled = false;
            Check(!audio.isPlaying, "Disabling the briefing stops its pending audio");
            Object.Destroy(slab.gameObject);

            var menu = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Audio/MainMenuAudio.prefab"));
            var menuMusic = menu.GetComponentInChildren<AudioSource>();
            Check(menuMusic.isPlaying && menuMusic.clip.name == "main_menu" && menuMusic.outputAudioMixerGroup.name == "Music",
                "Returning to menu playback uses the menu track and the same Music bus");
            Check(Object.FindObjectsByType<AudioSource>().All(s => s.clip == null || s.clip.name != "combat"),
                "No gameplay music survives outside its level");
            Time.timeScale = 1f;
            Debug.Log("LEVEL AND QUEST AUDIO CHECKS PASSED (scene placement and runtime playback)");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Time.timeScale = 1f; Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
