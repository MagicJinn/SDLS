using Sunless.Game.UI.Menus;
using Sunless.Game.Import;
using Sunless.Game.ApplicationProviders;
using System.Reflection;
using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;


namespace SDLS
{
    internal sealed class AutoImporter : MonoBehaviour
    {
        private static AutoImporter _instance;
        public static AutoImporter Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject gobj = new GameObject("AutoImporter");
                    _instance = gobj.AddComponent<AutoImporter>();
                    DontDestroyOnLoad(gobj);
                }
                return _instance;
            }
        }

        private bool _inTitleScreen = false;

        private void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _inTitleScreen = scene.name == "TitleScreen";
        }

        public bool CheckForNewContent()
        {
            if (!Importer.Instance.CheckIfNewContent()) return false;

            Plugin.Instance.Log("New Stories available, updating them...");
            StartCoroutine(WaitForMenuAndUpdate());

            return true;
        }

        // This method is a mess, since MainMenu doesn't have an Instance, instead its stored in MenuProvider as a private field.
        private IEnumerator WaitForMenuAndUpdate()
        {
            while (!_inTitleScreen)
            {
                yield return null;
            }
            // Get the singleton instance of MenuProvider
            var menuProvider = MenuProvider.Instance;
            if (menuProvider == null)
            {
                Plugin.Instance.Error("MenuProvider.Instance is null??");
                yield break;
            }

            // Get the private field '_mainMenu' from MenuProvider instance
            FieldInfo mainMenuField = typeof(MenuProvider).GetField("_mainMenu", BindingFlags.NonPublic | BindingFlags.Instance);
            if (mainMenuField == null)
            {
                Plugin.Instance.Error("Field '_mainMenu' not found in MenuProvider??");
                yield break;
            }

            // Get the MainMenu instance stored in _mainMenu
            if (mainMenuField.GetValue(menuProvider) is not MainMenu mainMenuInstance)
            {
                Plugin.Instance.Error("_mainMenu instance is null??");
                yield break;
            }

            // Get the private method 'UpdateContent' from MainMenu class
            MethodInfo updateContentMethod = typeof(MainMenu).GetMethod("UpdateContent", BindingFlags.NonPublic | BindingFlags.Instance);
            if (updateContentMethod == null)
            {
                Plugin.Instance.Error("Method 'UpdateContent' not found in MainMenu");
                yield break;
            }

            // Invoke UpdateContent on the MainMenu instance
            updateContentMethod.Invoke(mainMenuInstance, null);

            Destroy(gameObject);
        }
    }
}