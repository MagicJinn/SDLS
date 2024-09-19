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

        public bool isInitializationComplete = false; // Track whether regular SDLS initialization is complete
        private ManualResetEvent initializationCompletedEvent; // Event to signal when SDLS initialization is complete
        private bool isRepositoryManagerInitComplete = false; // Track whether RepositoryManager is initialized
        List<Canvas> disabledCanvases = new List<Canvas>();

        public void Initialize(ManualResetEvent initEvent)
        {
            initializationCompletedEvent = initEvent;
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
            // Hide UI elements until the Repository initiation is complete
            yield return new WaitForEndOfFrame();
            DisableAllUIElements();
            yield return new WaitUntil(() => isInitializationComplete && isRepositoryManagerInitComplete);
            EnableAllUIElements();
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
            float elapsedTime = 0f; while (elapsedTime < FadeDuration)
            {
                elapsedTime += Time.deltaTime;
                canvasGroup.alpha = Mathf.Clamp01(elapsedTime / FadeDuration);
                yield return null;
            }
            Destroy(gameObject); // Destroy the FastLoad object once everything is done
        }
    }
}