using Sunless.Game.Data;
using UnityEngine;
using System.Collections;
using System.Threading;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;

namespace SDLS
{
    public class FastLoad : MonoBehaviour
    {
        private const float FadeDuration = 2.0f; // Fade duration in seconds
        private const string TransitionPanelName = "TransitionPanel";

        // Variables related to muting the background music while loading
        private bool keepMuted = false;
        private AudioSource backgroundMusic;
        private Coroutine muteCoroutine;

        public bool isInitializationComplete = false; // Track whether regular SDLS initialization is complete
        private ManualResetEvent initializationCompletedEvent; // Event to signal when SDLS initialization is complete
        private bool isRepositoryManagerInitComplete = false; // Track whether RepositoryManager is initialized
        List<Canvas> disabledCanvases = new();

        public void Initialize(ManualResetEvent initEvent)
        {
            initializationCompletedEvent = initEvent;

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        public IEnumerator WaitForInitAndStartRepositoryManager()
        {
            yield return new WaitUntil(() => initializationCompletedEvent.WaitOne(0));
            ThreadPool.QueueUserWorkItem(_ => InitializeRepositoryManager());
        }

        private void InitializeRepositoryManager()
        {
            Plugin.Instance.DebugTimer("FastLoad");
            RepositoryManager.Instance.Initialise();
            isRepositoryManagerInitComplete = true;
            Plugin.Instance.DebugTimer("FastLoad");
        }

        public void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Hide UI elements when switching to the title screen
            if (scene.name == "TitleScreen") StartCoroutine(HideUIUntilInitComplete());
        }

        private IEnumerator HideUIUntilInitComplete()
        {
            yield return new WaitForEndOfFrame();
            DisableAllUIElements();
            StartForceMuteBackgroundMusic();
            yield return new WaitUntil(() => isInitializationComplete && isRepositoryManagerInitComplete);
            EnableAllUIElements();
            UnmuteAndRestartBackgroundMusic();
        }

        private void StartForceMuteBackgroundMusic()
        {
            AudioSource[] audioSources = FindObjectsOfType<AudioSource>(); // Get audiosources
            // Find the audiosource playing the main menu music
            backgroundMusic = audioSources.FirstOrDefault(source => source.clip != null && source.clip.name == "OpeningScreenNoMelody");

            if (backgroundMusic != null)
            {
                keepMuted = true;

                muteCoroutine = StartCoroutine(ForceMuteCoroutine());
            }
            else Logging.Warn("No background music AudioSource found to mute.");

        }

        private IEnumerator ForceMuteCoroutine()
        {
            while (keepMuted && backgroundMusic != null)
            {
                backgroundMusic.volume = 0f;
                yield return null; // Wait for the next frame
            }
        }

        private void UnmuteAndRestartBackgroundMusic()
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

        private void DisableAllUIElements()
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

        private void EnableAllUIElements()
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

            float elapsedTime = 0f; while (elapsedTime < FadeDuration && canvasGroup != null && canvasGroup.gameObject.activeInHierarchy)
            {
                elapsedTime += Time.deltaTime;
                canvasGroup.alpha = Mathf.Clamp01(elapsedTime / FadeDuration);
                yield return null;
            }

            // Only remove the event listener and destroy the object if we've completed the fade
            if (elapsedTime >= FadeDuration)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded; // Remove the scene loaded event listener
                Destroy(gameObject); // Destroy the FastLoad object once everything is done
            }
        }

    }
}