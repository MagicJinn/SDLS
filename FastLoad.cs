using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Threading;
using UnityEngine.SceneManagement;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using Sunless.Game.Data;
using Sunless.Game.Data.SNRepositories;
using Sunless.Game.Data.S3Repositories;
using Sunless.Game.UI.Components;
using Sunless.Game.Scripts.UI.Intro;
using Sunless.Game.Utilities;

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

            // Patch the RepositoryManager to load heavy hitters in the background
            Harmony.CreateAndPatchAll(typeof(RepositoryManagerInitialisePatch));
        }

        [HarmonyPatch(typeof(IntroScript), "Start")]
        private static class IntroScriptStartPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(IntroScript __instance)
            {
                backgroundTitleScreenLoad = SceneManager.LoadSceneAsync("TitleScreen"); // Load async in the background
                backgroundTitleScreenLoad.allowSceneActivation = false; // Prevent automatic activation
                return true; // Run the original start method
            }
        }
        [HarmonyPatch(typeof(IntroScript), "LoadTitleScreen")]
        private static class IntroScriptLoadTitleScreenPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(IntroScript __instance)
            {
                backgroundTitleScreenLoad.allowSceneActivation = true; // Allow the scene to activate
                return false; // Skip the original function
            }
        }

        [HarmonyPatch(typeof(RepositoryManager), "Initialise", [typeof(bool)])]
        private static class RepositoryManagerInitialisePatch
        {
            [HarmonyPrefix]
            private static bool Prefix(RepositoryManager __instance, bool reload, ref bool __result)
            {
                if (__instance._initialised && !reload)
                {
                    __result = true;
                    return false; // Skip the original method
                }
                __instance._initialised = true;

                // Array of repository instances to process
                var repositories = new object[]
                {
                    // QualityRepository.Instance, purposely skipped to load in the background
                    AreaRepository.Instance,
                    // EventRepository.Instance, purposely skipped to load in the background
                    ExchangeRepository.Instance,
                    PersonaRepository.Instance,
                    TileRulesRepository.Instance,
                    TileRepository.Instance,
                    TileSetRepository.Instance,
                    CombatAttackRepository.Instance,
                    CombatItemRepository.Instance,
                    SpawnedEntityRepository.Instance,
                    AssociationsRepository.Instance,
                    TutorialRepository.Instance,
                    NavigationConstantsRepository.Instance,
                    CombatConstantsRepository.Instance,
                    PromoDataRepository.Instance,
                    FlavourRepository.Instance
                };

                var eventRepositoryMRE = new ManualResetEvent(false);
                var qualityRepositoryMRE = new ManualResetEvent(false);

                ThreadPool.QueueUserWorkItem(_ => // Load EventRepository in the background
                {
                    EventRepository.Instance.Load(reload);
                    eventRepositoryMRE.Set(); // set as complete
                });

                ThreadPool.QueueUserWorkItem(_ => // Load QualityRepository in the background
                {
                    QualityRepository.Instance.Load(reload);
                    qualityRepositoryMRE.Set(); // set as complete
                });

                // Call Load on all repositories using reflection
                foreach (var repository in repositories)
                {
                    MethodInfo loadMethod = repository
                        .GetType()
                            .GetMethod("Load", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [typeof(bool)], null);

                    if (loadMethod != null) loadMethod.Invoke(repository, [reload]);
                    else Plugin.Instance.Warn($"Load method not found on {repository.GetType().Name}");
                }

                // Wait for these two to finish loading, then hydrate them
                eventRepositoryMRE.WaitOne();
                qualityRepositoryMRE.WaitOne();
                EventRepository.Instance.HydrateAll();
                QualityRepository.Instance.HydrateAll();

                // Call HydrateAll on all repositories using reflection
                foreach (var repository in repositories)
                {
                    MethodInfo hydrateMethod = repository
                        .GetType()
                            .GetMethod("HydrateAll", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (hydrateMethod != null) hydrateMethod.Invoke(repository, null);
                    else
                    {
                        string repositoryName = repository.GetType().Name;
                        var exclusionList = new[] { "NavigationConstantsRepository", "CombatConstantsRepository", "PromoDataRepository" }; // These don't have HydrateAll methods
                        if (!exclusionList.Contains(repositoryName)) Plugin.Instance.Log($"HydrateAll method not found on {repositoryName}.");
                    }
                }

                __result = true;
                return false; // Skip the original method
            }
        }
    }

    internal sealed class SDLSUIAndSceneManager : MonoBehaviour
    {
        private static SDLSUIAndSceneManager _instance;
        public static SDLSUIAndSceneManager Instance
        {
            get
            {
                if (_instance == null) // Singleton pattern to create a single instance
                {
                    GameObject gobj = new GameObject("SDLSUIAndSceneManager");
                    _instance = gobj.AddComponent<SDLSUIAndSceneManager>();
                    DontDestroyOnLoad(gobj);
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
            else Plugin.Instance.Warn("No background music AudioSource found to mute.");
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
                SDLSUIAndSceneManager.Instance.DisableAllUIElements();
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

        private GameObject progressBarCanvas;
        private ProgressBar progressBar;

        // How many entities there usually are to be loaded
        // Close enough for progress bar purposes
        const int totalExpectedEntities = 7624;

        private readonly List<Type> DataRepositories =
        [
            typeof(QualityRepository),
            typeof(AreaRepository),
            typeof(EventRepository),
            typeof(ExchangeRepository),
            typeof(PersonaRepository),
            typeof(TileRulesRepository),
            typeof(TileRepository),
            typeof(TileSetRepository),
            typeof(CombatAttackRepository),
            typeof(CombatItemRepository),
            typeof(SpawnedEntityRepository),
            typeof(AssociationsRepository),
            typeof(TutorialRepository),
            typeof(FlavourRepository)
        ];

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
            EnsureFilesCopiedFromDLLSafely(); // Prevent https://github.com/MagicJinn/SDLS/issues/4 from occurring
            ThreadPool.QueueUserWorkItem(_ => InitializeRepositoryManager());
        }

        private void EnsureFilesCopiedFromDLLSafely()
        {
            // Array of repository instances to process
            var repositories = new object[]
            {
            QualityRepository.Instance,
            AreaRepository.Instance,
            EventRepository.Instance,
            ExchangeRepository.Instance,
            PersonaRepository.Instance,
            TileRulesRepository.Instance,
            TileRepository.Instance,
            TileSetRepository.Instance,
            CombatAttackRepository.Instance,
            CombatItemRepository.Instance,
            SpawnedEntityRepository.Instance,
            AssociationsRepository.Instance,
            TutorialRepository.Instance,
            NavigationConstantsRepository.Instance,
            CombatConstantsRepository.Instance,
            PromoDataRepository.Instance,
            FlavourRepository.Instance
            };

            foreach (var repository in repositories)
            {
                // Use reflection to access protected EnsureCopiedFromDll method
                MethodInfo method = repository
                    .GetType()
                        .GetMethod(
                            "EnsureCopiedFromDll",
                            BindingFlags.Instance |
                            BindingFlags.NonPublic);
                if (method != null) method.Invoke(repository, null);
                else Plugin.Instance.Warn($"EnsureCopiedFromDll failed on {repository.GetType().Name}");
            }
        }

        private void InitializeRepositoryManager()
        {
            try
            {
                Plugin.Instance.DebugTimer("FastLoad");
                RepositoryManager.Instance.Initialise();
            }
            catch (Exception ex)
            { Plugin.Instance.Error("Failed to initialize the RepositoryManager. Error: " + ex.Message); }

            isRepositoryManagerInitComplete = true;
            Plugin.Instance.DebugTimer("FastLoad");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Hide UI elements when switching to the title screen
            if (scene.name == "TitleScreen")
            {
                StartCoroutine(HideUIUntilInitComplete());
            }
        }

        private IEnumerator RepositoryLoadProgressbar()
        {
            // Create a new Canvas for the progress bar, since all other canvases are disabled
            progressBarCanvas = new GameObject("ProgressBarCanvas");
            var canvas = progressBarCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            progressBarCanvas.AddComponent<CanvasScaler>();

            progressBar = new ProgressBar(progressBarCanvas, () => { })
            { Message = "Loading...", };
            progressBar.InnerGameObject.FindComponentInDescendant<Button>("Exit").gameObject.SetActive(false); // Hide the exit button
            progressBar.InnerGameObject.FindComponentInDescendant<Image>("Splitter").gameObject.SetActive(false); // Hide the exit button

            var progressbarRectTransformInner = progressBar.InnerGameObject.GetComponent<RectTransform>();
            if (progressbarRectTransformInner.rect.width == 0 || progressbarRectTransformInner.rect.height == 0) yield return new WaitForEndOfFrame(); // Wait until the next frame for RectTransform to be initialized
            float progressBarWidth = progressbarRectTransformInner.rect.width;
            float progressBarHeight = progressbarRectTransformInner.rect.height;

            progressbarRectTransformInner.anchorMin = progressbarRectTransformInner.anchorMax = new Vector2(1, 0); // Anchor to bottom right corner

            // Offset the position based on the width and height of the progress bar
            progressbarRectTransformInner.anchoredPosition = new Vector2(-progressBarWidth / 2, progressBarHeight / 2);

            StartCoroutine(CheckRepositoriesCoroutine(progressBar)); // Update the progress bar based on repository initialization
        }

        private IEnumerator HideUIUntilInitComplete()
        {
            yield return new WaitForEndOfFrame();
            SDLSUIAndSceneManager.Instance.DisableAllUIElements();
            SDLSUIAndSceneManager.Instance.StartForceMuteBackgroundMusic();
            StartCoroutine(RepositoryLoadProgressbar());

            yield return new WaitUntil(() => Plugin.jsonInitializationComplete && isRepositoryManagerInitComplete);
            SDLSUIAndSceneManager.Instance.EnableAllUIElements();
            SDLSUIAndSceneManager.Instance.UnmuteAndRestartBackgroundMusic();

            if (FindObjectOfType<LoadIntoSave>() == null) // Dirty check if LoadIntoSave exists
            {
                StartCoroutine(DestroyProgressBar());
            }
        }

        private IEnumerator DestroyProgressBar()
        {
            progressBar.Message = "Loading complete!";
            float moveSpeed = 0f;
            var rectTransform = progressBar.InnerGameObject.GetComponent<RectTransform>();
            bool moveUp = UnityEngine.Random.value < 0.1f;
            float screenHeight = Screen.height; // Get screen height to determine when it's fully offscreen

            // Move the progress bar off screen slowly
            while (moveUp ? rectTransform.anchoredPosition.y < screenHeight + rectTransform.rect.height
                          : rectTransform.anchoredPosition.y > -rectTransform.rect.height)
            {
                rectTransform.anchoredPosition += new Vector2(0, moveUp ? moveSpeed : -moveSpeed);
                moveSpeed += 0.01f;
                yield return null;
            }
        }

        private IEnumerator CheckRepositoriesCoroutine(ProgressBar progressBar)
        {
            // Track old and new count to determine whether the repository is fully loaded (if stagnant at non-zero count, its loaded)
            var previousCounts = new Dictionary<string, int>();
            var currentCounts = new Dictionary<string, int>();
            var completedRepos = new HashSet<Type>();
            int loadedEntities = 0;

            while (DataRepositories.Count > completedRepos.Count) // Check whether all repositories are loaded
            {
                // Swap dictionaries to avoid unnecessary allocations
                var temp = previousCounts;
                previousCounts = currentCounts;
                currentCounts = temp;
                currentCounts.Clear();

                foreach (var repo in DataRepositories)
                {
                    if (completedRepos.Contains(repo)) continue;

                    int entityCount = GetRepositoryEntityCount(repo);
                    if (entityCount > 0)
                    {
                        currentCounts[repo.Name] = entityCount;

                        // Check if the count is stable
                        if (previousCounts.TryGetValue(repo.Name, out int prevCount) && prevCount == entityCount)
                        {
                            completedRepos.Add(repo);
                            loadedEntities += entityCount;
                        }
                    }
                }

                progressBar.PercentageComplete = Math.Max(loadedEntities * 100 / totalExpectedEntities, 100);
                yield return new WaitForSeconds(0.05f);
            }
        }

        private int GetRepositoryEntityCount(Type repo)
        {
            FieldInfo field = repo.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (field?.GetValue(null) is object repoInstance)
            {
                FieldInfo entitiesField = repoInstance.GetType().GetField("Entities",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

                if (entitiesField?.GetValue(repoInstance) is IDictionary dictionary)
                {
                    return dictionary.Count;
                }
            }
            return 0;
        }
    }
}
