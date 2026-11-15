
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Top-level coordinator for main menu / in-tour canvas switching and menu button wiring.
/// Subscribes to TourManager.OnTourChanged to drive canvas visibility
///
/// Sub-panels: each side button is one entry in Sub Panels (button, Gold active bar, panel).
/// At most one sub-panel is open at a time. While one is open its button shows the active bar
/// and the welcome panel is hidden; when none is open the welcome panel is shown.
/// An entry with no panel assigned is a placeholder button that does nothing.
/// Adding a sub-panel (for example the campus map) is an Inspector change only: derive the
/// panel from MenuSubPanel and assign it to the entry of the button that should open it.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    /// <summary>
    /// One side button and the sub-panel it opens.
    /// </summary>
    [Serializable]
    private class SubPanelBinding
    {
        [Tooltip("Side button that opens this entry's sub-panel. Required.")]
        public Button navButton;

        [Tooltip("Gold bar object on the side button. Active only while this entry's sub-panel is open. Required.")]
        public GameObject activeBar;

        [Tooltip("Sub-panel opened by the button. Leave empty for a placeholder button that does nothing.")]
        public MenuSubPanel panel;
    }

    #region Serialized Fields

    [Header("System References")]

    [Tooltip("Reference to the TourManager in the scene. Required.")]
    [SerializeField] private TourManager tourManager;

    [Tooltip("TourCatalog asset. Used to source the menu title text.")]
    [SerializeField] private TourCatalog tourCatalog;

    [Header("Menu Canvas")]

    [Tooltip("Root GameObject of the MenuCanvas. Active when no tour is loaded.")]
    [SerializeField] private GameObject menuCanvas;

    [Tooltip("Text element displaying the menu title. Populated from TourCatalog.menuTitle at runtime.")]
    [SerializeField] private TextMeshProUGUI titleText;

    [Header("Tour Overlay Canvas")]

    [Tooltip("Root GameObject of the TourOverlayCanvas. Active when a tour is loaded.")]
    [SerializeField] private GameObject tourOverlayCanvas;

    [Tooltip("Button on the tour overlay that opens the return-to-menu confirmation dialog.")]
    [SerializeField] private Button returnToMenuButton;

    [Header("Side Buttons and Sub-Panels")]

    [Tooltip("One entry per side button, in any order. Only one sub-panel is open at a time.")]
    [SerializeField] private List<SubPanelBinding> subPanels = new List<SubPanelBinding>();

    [Tooltip("Panel shown in the sub-panel area while no sub-panel is open.")]
    [SerializeField] private GameObject welcomePanel;

    [Header("Confirmation Dialog")]

    [Tooltip("Shared ConfirmationDialog used for the return-to-menu flow.")]
    [SerializeField] private ConfirmationDialog confirmationDialog;

    [Header("Messages")]

    [Tooltip("Message shown in the confirmation dialog when the user clicks Return to Menu.")]
    [SerializeField] private string returnConfirmationMessage = "Return to the main menu?";

    #endregion

    private bool _isReady;

    // The entry whose sub-panel is currently open, or null when the welcome panel is showing.
    private SubPanelBinding _openBinding;

    private void Awake()
    {
        _isReady = ValidateReferences();
    }

    private void Start()
    {
        if (!_isReady)
            return;

        // Wire side buttons and sub-panel close requests
        for (int i = 0; i < subPanels.Count; i++)
        {
            SubPanelBinding binding = subPanels[i];

            binding.navButton.onClick.AddListener(() => OpenSubPanel(binding));

            if (binding.panel != null)
                binding.panel.CloseRequested += CloseOpenSubPanel;
        }

        returnToMenuButton.onClick.AddListener(HandleReturnToMenuClicked);

        // Title text is sourced from the catalog
        titleText.text = tourCatalog.menuTitle;

        // Apply initial visibility from current state. Also hides every sub-panel.
        ApplyCanvasVisibility(tourManager.CurrentTour);

        // Subscribe to tour state changes.
        tourManager.OnTourChanged += HandleTourChanged;
    }

    private void OnDestroy()
    {
        if (tourManager != null)
            tourManager.OnTourChanged -= HandleTourChanged;

        for (int i = 0; i < subPanels.Count; i++)
        {
            SubPanelBinding binding = subPanels[i];

            if (binding != null && binding.panel != null)
                binding.panel.CloseRequested -= CloseOpenSubPanel;
        }
    }

    #region Handlers

    private void HandleTourChanged(TourData tour)
    {
        ApplyCanvasVisibility(tour);
    }

    private void HandleReturnToMenuClicked()
    {
        confirmationDialog.Show(
            returnConfirmationMessage,
            onConfirm: HandleReturnConfirmed,
            onCancel: null);
    }

    private void HandleReturnConfirmed()
    {
        tourManager.UnloadTour();
    }

    #endregion

    #region Sub-Panels

    /// <summary>
    /// Opens the given entry's sub-panel, closing any other open sub-panel first.
    /// Does nothing for a placeholder entry (no panel) or for the entry that is already open.
    /// </summary>
    private void OpenSubPanel(SubPanelBinding binding)
    {
        if (binding.panel == null || binding == _openBinding)
            return;

        if (_openBinding != null)
            _openBinding.panel.Close();

        _openBinding = binding;
        binding.panel.Open();

        RefreshSubPanelIndicators();
    }

    /// <summary>
    /// Closes the open sub-panel, if any, and returns to the welcome panel.
    /// Also the handler for MenuSubPanel.CloseRequested.
    /// </summary>
    private void CloseOpenSubPanel()
    {
        if (_openBinding != null)
        {
            _openBinding.panel.Close();
            _openBinding = null;
        }

        RefreshSubPanelIndicators();
    }

    /// <summary>
    /// Hides every registered sub-panel regardless of tracked state. Used at startup, where
    /// panels may have been saved active in the scene, and whenever the tour changes.
    /// </summary>
    private void CloseAllSubPanels()
    {
        for (int i = 0; i < subPanels.Count; i++)
        {
            if (subPanels[i].panel != null)
                subPanels[i].panel.Close();
        }

        _openBinding = null;

        RefreshSubPanelIndicators();
    }

    /// <summary>
    /// Shows the active bar only on the open entry's button, and the welcome panel only
    /// while no sub-panel is open.
    /// </summary>
    private void RefreshSubPanelIndicators()
    {
        for (int i = 0; i < subPanels.Count; i++)
            subPanels[i].activeBar.SetActive(subPanels[i] == _openBinding);

        welcomePanel.SetActive(_openBinding == null);
    }

    #endregion

    #region Private Methods

    private void ApplyCanvasVisibility(TourData tour)
    {
        bool tourLoaded = tour != null;

        menuCanvas.SetActive(!tourLoaded);
        tourOverlayCanvas.SetActive(tourLoaded);

        CloseAllSubPanels();
    }

    #endregion

    #region Validation

    private bool ValidateReferences()
    {
        bool valid = true;

        if (tourManager == null)
        {
            Debug.LogError("[MainMenuController] TourManager reference is not assigned.");
            valid = false;
        }

        if (tourCatalog == null)
        {
            Debug.LogError("[MainMenuController] TourCatalog reference is not assigned.");
            valid = false;
        }

        if (menuCanvas == null)
        {
            Debug.LogError("[MainMenuController] MenuCanvas reference is not assigned.");
            valid = false;
        }

        if (titleText == null)
        {
            Debug.LogError("[MainMenuController] TitleText reference is not assigned.");
            valid = false;
        }

        if (tourOverlayCanvas == null)
        {
            Debug.LogError("[MainMenuController] TourOverlayCanvas reference is not assigned.");
            valid = false;
        }

        if (returnToMenuButton == null)
        {
            Debug.LogError("[MainMenuController] ReturnToMenuButton reference is not assigned.");
            valid = false;
        }

        if (subPanels == null || subPanels.Count == 0)
        {
            Debug.LogError("[MainMenuController] Sub Panels list is empty. Add one entry per side button.");
            valid = false;
        }
        else
        {
            for (int i = 0; i < subPanels.Count; i++)
            {
                SubPanelBinding binding = subPanels[i];

                if (binding == null || binding.navButton == null)
                {
                    Debug.LogError($"[MainMenuController] Sub Panels entry {i}: Nav Button is not assigned.");
                    valid = false;
                    continue;
                }

                if (binding.activeBar == null)
                {
                    Debug.LogError($"[MainMenuController] Sub Panels entry {i}: Active Bar is not assigned.");
                    valid = false;
                }

                // binding.panel may be empty: that entry is a placeholder button.
            }
        }

        if (welcomePanel == null)
        {
            Debug.LogError("[MainMenuController] WelcomePanel reference is not assigned.");
            valid = false;
        }

        if (confirmationDialog == null)
        {
            Debug.LogError("[MainMenuController] ConfirmationDialog reference is not assigned.");
            valid = false;
        }

        return valid;
    }

    #endregion
}
