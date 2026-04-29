using System;
using System.Collections.Generic;
using UnityEngine;

namespace AGVRSystem.UI
{
    /// <summary>
    /// Singleton that manages the active color theme.
    /// Cycles through registered themes and notifies all listeners on change.
    /// Persists the selected theme index across sessions via PlayerPrefs.
    /// </summary>
    public class ThemeManager : MonoBehaviour
    {
        [SerializeField] private List<ThemeData> _themes = new List<ThemeData>();
        [SerializeField] private int _defaultThemeIndex;

        private int _currentIndex;
        private static ThemeManager _instance;

        private const string ThemePrefKey = "SelectedThemeIndex";

        /// <summary>Singleton accessor.</summary>
        public static ThemeManager Instance => _instance;

        /// <summary>Currently active theme.</summary>
        public ThemeData CurrentTheme =>
            _themes != null && _themes.Count > 0
                ? _themes[Mathf.Clamp(_currentIndex, 0, _themes.Count - 1)]
                : null;

        /// <summary>Raised whenever the active theme changes. Passes the new ThemeData.</summary>
        public event Action<ThemeData> OnThemeChanged;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                // Destroy ONLY this component — NOT the gameObject.
                // ThemeManager lives on the shared /Managers root which also
                // parents SessionManager, ExerciseCoordinator, HandJointVisualizer, etc.
                // Destroying the gameObject would wipe out the entire scene hierarchy.
                Destroy(this);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            _currentIndex = PlayerPrefs.GetInt(ThemePrefKey, _defaultThemeIndex);
            _currentIndex = Mathf.Clamp(_currentIndex, 0, Mathf.Max(0, _themes.Count - 1));
        }

        /// <summary>
        /// Cycles to the next theme in the list and notifies listeners.
        /// </summary>
        public void CycleTheme()
        {
            if (_themes == null || _themes.Count <= 1)
                return;

            _currentIndex = (_currentIndex + 1) % _themes.Count;
            PlayerPrefs.SetInt(ThemePrefKey, _currentIndex);
            PlayerPrefs.Save();

            OnThemeChanged?.Invoke(CurrentTheme);
        }

        /// <summary>
        /// Sets a specific theme by index and notifies listeners.
        /// </summary>
        public void SetTheme(int index)
        {
            if (_themes == null || _themes.Count == 0)
                return;

            _currentIndex = Mathf.Clamp(index, 0, _themes.Count - 1);
            PlayerPrefs.SetInt(ThemePrefKey, _currentIndex);
            PlayerPrefs.Save();

            OnThemeChanged?.Invoke(CurrentTheme);
        }

        /// <summary>
        /// Returns the name of the next theme (for button label).
        /// </summary>
        public string GetNextThemeName()
        {
            if (_themes == null || _themes.Count <= 1)
                return string.Empty;

            int nextIdx = (_currentIndex + 1) % _themes.Count;
            return _themes[nextIdx] != null ? _themes[nextIdx].ThemeName : string.Empty;
        }
    }
}
