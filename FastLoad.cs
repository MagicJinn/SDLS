using Sunless.Game.Data;
using UnityEngine;
using System.Collections;
using System.Threading;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;
using System;
using HarmonyLib;
using Sunless.Game.Scripts.UI.Intro;

namespace SDLS
{
    internal sealed class PatchMethodsForPerformance
    {
        private static AsyncOperation backgroundTitleScreenLoad;

        public static void DoPerformancePatches()
        {
            // Patch the incorrect use of LoadSceneAsync
            Harmony.CreateAndPatchAll(typeof(IntroScriptStartPatch));
            Harmony.CreateAndPatchAll(typeof(IntroScriptLoadTitleScreenPatch));
        }

        [HarmonyPatch(typeof(IntroScript), "Start")]
        private static class IntroScriptStartPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(IntroScript __instance)
            {
                backgroundTitleScreenLoad = SceneManager.LoadSceneAsync("TitleScreen"); // Load async in the background
                backgroundTitleScreenLoad.allowSceneActivation = false; // Prevent automatic activation
                return true; // Run the original function
            }
        }
        [HarmonyPatch(typeof(IntroScript), "LoadTitleScreen")]
        private static class IntroScriptLoadTitleScreenPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(IntroScript __instance)
            {
                backgroundTitleScreenLoad.allowSceneActivation = true;
                return false; // Skip the original function
            }
        }
    }

    internal sealed class SDLSUIAndSceneManager : MonoBehaviour
    {
        private static SDLSUIAndSceneManager _instance;
        public static SDLSUIAndSceneManager I
        {
            get
            {
                if (_instance == null)
                {
                    // Create a new GameObject and add the component
                    GameObject go = new GameObject("SDLSUIAndSceneManager");
                    _instance = go.AddComponent<SDLSUIAndSceneManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private const string TransitionPanelName = "TransitionPanel";
        private const float FadeDuration = 1.5f; // Fade duration in seconds
        List<Canvas> disabledCanvases = new();

        // Variables related to muting the background music while loading
        private bool keepMuted = false;
        private AudioSource backgroundMusic;
        private Coroutine muteCoroutine;

        public void DisableAllUIElements()
        {
            // Filter out the transition panel
            disabledCanvases = FindObjectsOfType<Canvas>()
                .Where(canvas => !canvas.name.Contains(TransitionPanelName))
                .ToList();

            foreach (var canvas in disabledCanvases)
            {
                canvas.enabled = false; // Disable canvas
            }
        }

        public void StartForceMuteBackgroundMusic()
        {
            AudioSource[] audioSources = FindObjectsOfType<AudioSource>(); // Get audiosources
            // Find the audiosource playing the main menu music
            backgroundMusic = audioSources.FirstOrDefault(source => source.clip != null && source.clip.name == "OpeningScreenNoMelody");

            if (backgroundMusic != null)
            {
                keepMuted = true;
                muteCoroutine = StartCoroutine(ForceMuteCoroutine());
            }
            else Plugin.I.Warn("No background music AudioSource found to mute.");
        }

        private IEnumerator ForceMuteCoroutine()
        {
            while (keepMuted && backgroundMusic != null)
            {
                backgroundMusic.volume = 0f;
                yield return null; // Wait for the next frame
            }
        }

        public void UnmuteAndRestartBackgroundMusic()
        {
            keepMuted = false;
            if (muteCoroutine != null)
            {
                StopCoroutine(muteCoroutine);
                muteCoroutine = null; // Delete muteCoroutine
            }

            backgroundMusic.volume = 1f; // Reset original volume
            backgroundMusic.time = 0f; // Reset to the beginning of the track
        }

        public void EnableAllUIElements()
        {
            foreach (var canvas in disabledCanvases)
            {
                canvas.enabled = true; // Enable canvas
                var canvasGroup = canvas.GetComponent<CanvasGroup>() ?? canvas.gameObject.AddComponent<CanvasGroup>();
                canvasGroup.alpha = 0; // Make canvas invisible
                StartCoroutine(FadeInCanvas(canvasGroup)); // Fade in canvas
            }
        }


        private IEnumerator FadeInCanvas(CanvasGroup canvasGroup)
        {
            if (canvasGroup == null || !canvasGroup.gameObject.activeInHierarchy) yield break; // Exit if canvasGroup is null or inactive

            float elapsedTime = 0f;
            while (elapsedTime < FadeDuration && canvasGroup != null && canvasGroup.gameObject.activeInHierarchy)
            {
                elapsedTime += Time.deltaTime;
                canvasGroup.alpha = Mathf.Clamp01(elapsedTime / FadeDuration);
                yield return null;
            }

            // Only remove the event listener and destroy the object if we've completed the fade
            if (elapsedTime >= FadeDuration)
            {
                Destroy(gameObject); // Destroy the FastLoad object once everything is done
            }
        }
    }

    internal sealed class LoadIntoSave : MonoBehaviour
    {
        private void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        public IEnumerator LoadIntoSaveCoroutine(FastLoad fastLoader)
        {
            // Determine the condition based on fastLoader's state
            if (fastLoader != null) // Check whether Fastload is activated
            {
                yield return new WaitUntil(() => fastLoader.isRepositoryManagerInitComplete); // Wait until all repositories are loaded
            }
            else
            {
                yield return new WaitUntil(() => Plugin.jsonInitializationComplete); // Wait until SDLS JSON has been processed
            }
            LoadMostRecentSave();
        }

        private void LoadMostRecentSave()
        {
            string characterName = CharacterRepository.Instance.GetCharacterNames(true).FirstOrDefault();
            if (characterName != null) // A save exists
                CharacterRepository.Instance.LoadGame(characterName);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Hide UI elements when switching to the title screen
            if (scene.name == "TitleScreen")
            {
                SDLSUIAndSceneManager.I.DisableAllUIElements();
            }
            else if (scene.name == "Loading" || scene.name == "Sailing")
            {
                Destroy(gameObject);
            }
        }
    }

    internal sealed class FastLoad : MonoBehaviour
    {
        public bool isRepositoryManagerInitComplete = false; // Track whether RepositoryManager is initialized

        private void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        public IEnumerator WaitForInitAndStartRepositoryManager()
        {
            yield return new WaitUntil(() => Plugin.jsonInitializationComplete);
            ThreadPool.QueueUserWorkItem(_ => InitializeRepositoryManager());
        }

        private void InitializeRepositoryManager()
        {
            try
            {
                Plugin.I.DebugTimer("FastLoad");
                RepositoryManager.Instance.Initialise();
            }
            catch (Exception ex)
            {
                Plugin.I.Error("Failed to initialize the RepositoryManager. Error: " + ex.Message);
            }
            finally
            {
                isRepositoryManagerInitComplete = true;
                Plugin.I.DebugTimer("FastLoad");
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Hide UI elements when switching to the title screen
            if (scene.name == "TitleScreen") StartCoroutine(HideUIUntilInitComplete());
        }

        private IEnumerator HideUIUntilInitComplete()
        {
            yield return new WaitForEndOfFrame();
            SDLSUIAndSceneManager.I.DisableAllUIElements();
            SDLSUIAndSceneManager.I.StartForceMuteBackgroundMusic();
            yield return new WaitUntil(() => Plugin.jsonInitializationComplete && isRepositoryManagerInitComplete);
            SDLSUIAndSceneManager.I.EnableAllUIElements();
            SDLSUIAndSceneManager.I.UnmuteAndRestartBackgroundMusic();
        }
    }
}