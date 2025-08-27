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
using Sunless.Game.ApplicationProviders;
using System.IO;
using Ionic.Zip;
using System.Text;
using Sunless.Game.Entities.Geography;
using Sunless.Game.Scripts.UI;
using Sunless.Game.UI.Map;

namespace SDLS;

internal sealed class PatchMethodsForPerformance
{
    public static AsyncOperation backgroundTitleScreenLoad;

    public static void DoPerformancePatches()
    {
        // Patch the incorrect use of LoadSceneAsync
        Harmony.CreateAndPatchAll(typeof(IntroScriptStartPatch));
        Harmony.CreateAndPatchAll(typeof(IntroScriptLoadTitleScreenPatch));

        // Patch the RepositoryManager to load heavy hitters in the background
        Harmony.CreateAndPatchAll(typeof(RepositoryManagerInitialisePatch));

        // Patch FileHelper to not overwrite existing files
        Harmony.CreateAndPatchAll(typeof(FileHelperCopyFilesFromEmbeddedArchiveToFileSystemPatch));

        // Patch the fog generation to be faster
        Harmony.CreateAndPatchAll(typeof(MapProviderGeneratePatch));
    }

    [HarmonyPatch(typeof(MapProvider), "Generate")]
    private static class MapProviderGeneratePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(MapProvider __instance)
        {
            __instance._mapContainer = GameObject.Find("MapContainer");
            if (__instance._mapContainer != null)
            {
                __instance._MapTileWidth = __instance.CurrentCharacter.TileConfig.Width;
                __instance._MapTileHeight = __instance.CurrentCharacter.TileConfig.Height;
                __instance._mapWidth = __instance._MapTileWidth * __instance.MapTileHeight;
                __instance._mapHeight = __instance._MapTileHeight * __instance.MapTileHeight;
                if (__instance._mapWidth / __instance._mapHeight > __instance._aspectRatio)
                {
                    __instance._mapBaseScale = new Vector2(__instance._MapTileWidth + 1, (__instance._MapTileWidth + 1f) / __instance._aspectRatio);
                }
                else
                {
                    __instance._mapBaseScale = new Vector2((__instance._MapTileHeight + 1f) * __instance._aspectRatio, __instance._MapTileHeight + 1);
                }
                __instance._mapBaseSize = new Vector2(__instance._mapBaseScale.x * __instance.MapTileHeight, __instance._mapBaseScale.y * __instance.MapTileHeight);
                __instance.GetFogRevealPieces();
                GameObject gameObject = __instance._mapContainer.FindDescendant("MapBordered");
                gameObject.transform.localScale = new Vector3(__instance._mapBaseScale.x, __instance._mapBaseScale.y, gameObject.transform.localScale.z);
                __instance._mapRoot = UnityEngine.Object.Instantiate<GameObject>(PrefabHelper.Instance.Get("Map/Map"));
                __instance._mapRoot.name = "Map";
                __instance._mapRoot.transform.parent = __instance._mapContainer.transform;
                __instance._mapRoot.transform.Translate(-__instance._mapWidth / 2f, -__instance._mapHeight / 2f, 0f);
                GameObject gameObject3 = __instance._mapRoot.FindDescendant("Background");
                gameObject3.transform.localScale = new Vector3(__instance._MapTileWidth, __instance._MapTileHeight, 0f);
                Material material = gameObject3.GetComponent<Renderer>().material;
                if (material != null)
                {
                    material.mainTextureScale = new Vector2(__instance._MapTileWidth / 2f, __instance._MapTileHeight);
                }
                GameObject gameObject4 = __instance._mapRoot.FindDescendant("Stripes");
                gameObject4.transform.localScale = new Vector3(__instance._MapTileWidth, __instance._MapTileHeight, 0f);
                Material material2 = gameObject4.GetComponent<Renderer>().material;
                if (material2 != null)
                {
                    material2.mainTextureScale = new Vector2(__instance._MapTileWidth, __instance._MapTileHeight);
                }
                GameObject gameObject2 = __instance._mapRoot.FindDescendant("Borders");
                if (gameObject2 != null)
                {
                    __instance.RescaleBorders(gameObject2);
                }
                __instance._mapFoU = __instance._mapRoot.FindDescendant("FoU");
                __instance._mapFoU.transform.localPosition = new Vector3(__instance._mapWidth / 2f, __instance._mapHeight / 2f, __instance._mapFoU.transform.localPosition.z);
                __instance._mapFoU.transform.localScale = new Vector3(__instance._MapTileWidth, __instance._MapTileHeight, 0f);
                __instance._icons = [];
                __instance._boatIcon = __instance._mapRoot.AddChildPreserveTransform(PrefabHelper.Instance.Get("Map/Icons/Ico_boat"));
                __instance._icons.Add(__instance._boatIcon);
                __instance._mapCamera = GameObject.Find("Map Camera").GetComponent<Camera>();
                __instance._tiles = [];
                __instance._tilesGrid = new TileInstance[__instance._MapTileHeight, __instance._MapTileWidth];
                __instance._terrain = [];
                __instance._labels = [];
                __instance._ports = [];
                __instance._labelsSmallGO = GameObject.Find("LabelsSmall");
                __instance._labelsSmallCanvas = __instance._labelsSmallGO.GetComponent<CanvasGroup>();
                __instance._labelsSmallCanvasFader = __instance._labelsSmallGO.GetComponent<CanvasFader>();
                __instance._labelsLargeGO = GameObject.Find("LabelsLarge");

                // Faster black pixel generation by batching it
                int width = 4 * __instance._MapTileWidth * 64;
                int height = 4 * __instance._MapTileHeight * 64;
                __instance._fogOfUnknown = new Texture2D(width, height)
                {
                    wrapMode = TextureWrapMode.Clamp
                };
                Color[] colors = new Color[width * height];
                for (int i = 0; i < colors.Length; i++)
                {
                    colors[i] = Color.black;
                }
                __instance._fogOfUnknown.SetPixels(colors);
                __instance._fogOfUnknown.Apply();

                __instance._mapControls = new MapControls(NavigationProvider.Instance.Anchors.TR);
                __instance.ToggleExitButton(false);
            }
            else
            {
                Debug.Log("Map could not be found!");
            }
            return false; // don't run the original method
        }
    }

    [HarmonyPatch(typeof(FileHelper), "CopyFilesFromEmbeddedArchiveToFileSystem")]
    private static class FileHelperCopyFilesFromEmbeddedArchiveToFileSystemPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(string archivePath, string fileSystemPath)
        {
            Plugin.DebugTimer("CopyFilesFromEmbeddedArchiveToFileSystem");
            FileHelper.EnsureDirectoryExists(fileSystemPath);
            Stream manifestResourceStream = FileHelper.GetAssembly().GetManifestResourceStream(archivePath);
            string fileSystemPath2 = GameProvider.Instance.GetApplicationPath(fileSystemPath);
            foreach (ZipEntry zipEntry in ZipFile.Read(manifestResourceStream, new ReadOptions { Encoding = Encoding.UTF8 }))
            {
                if (zipEntry.IsDirectory) continue;

                string fullPath = Path.Combine(fileSystemPath2, zipEntry.FileName);
                if (File.Exists(fullPath)) continue;

                FileStream stream = new(fullPath, FileMode.Create);
                zipEntry.Extract(stream);
            }
            Plugin.DebugTimer("CopyFilesFromEmbeddedArchiveToFileSystem");
            return false; // Don't run the original start method
        }
    }

    [HarmonyPatch(typeof(IntroScript), "Start")]
    private static class IntroScriptStartPatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
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
        private static bool Prefix()
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
                else Plugin.Warn($"Load method not found on {repository.GetType().Name}");
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
                    if (!exclusionList.Contains(repositoryName)) Plugin.Log($"HydrateAll method not found on {repositoryName}.");
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
                var gobj = new GameObject("SDLSUIAndSceneManager");
                _instance = gobj.AddComponent<SDLSUIAndSceneManager>();
                DontDestroyOnLoad(gobj);
            }
            return _instance;
        }
    }

    private const float FadeDuration = 1.5f; // Fade duration in seconds
    List<Canvas> disabledCanvases = [];

    // Variables related to muting the background music while loading
    private bool keepMuted = false;
    private AudioSource backgroundMusic;
    private Coroutine muteCoroutine;

    public void DisableAllUIElements()
    {
        // Filter out the transition panel
        disabledCanvases = [.. FindObjectsOfType<Canvas>().Where(canvas => !(canvas == TransitionProvider.Instance.TransitionCanvas))];

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
        else Plugin.Warn("No background music AudioSource found to mute.");
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

        if (backgroundMusic != null)
        {
            backgroundMusic.volume = 1f; // Reset original volume
            backgroundMusic.time = 0f; // Reset to the beginning of the track
        }
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
    private bool _inTitleScreen = false;

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

        // Load into the scene as soon as we're done with SDLS jobs
        // We need to load into the main menu, instead of just into the save, because otherwise the audio managers won't be ready yet
        PatchMethodsForPerformance.backgroundTitleScreenLoad.allowSceneActivation = true;

        yield return new WaitUntil(() => _inTitleScreen);

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
            _inTitleScreen = true;
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
            else Plugin.Warn($"EnsureCopiedFromDll failed on {repository.GetType().Name}");
        }
    }

    private void InitializeRepositoryManager()
    {
        try
        {
            Plugin.DebugTimer("FastLoad");
            RepositoryManager.Instance.Initialise();
        }
        catch (Exception ex)
        { Plugin.Error("Failed to initialize the RepositoryManager. Error: " + ex.Message); }

        isRepositoryManagerInitComplete = true;
        Plugin.DebugTimer("FastLoad");
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Hide UI elements when switching to the title screen
        if (scene.name == "TitleScreen")
        {
            StartCoroutine(HideUIUntilInitComplete());
        }
        else
        {
            DestroyProgressBar();
        }
    }

    private IEnumerator RepositoryLoadProgressbar()
    {
        // Create a new Canvas for the progress bar, since all other canvases are disabled by us
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

        if (progressBarCanvas == null) yield break; // Progress bar has been destroyed while we were waiting. Nothing to do.

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
            StartCoroutine(SunsetProgressBar());
        }
    }

    private IEnumerator SunsetProgressBar()
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
        DestroyProgressBar();
    }

    private void DestroyProgressBar()
    {
        Destroy(progressBarCanvas);
        progressBarCanvas = null;
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
