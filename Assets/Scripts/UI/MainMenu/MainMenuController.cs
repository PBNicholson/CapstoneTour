
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Top-level coordinator for main menu / in-tour canvas switching and menu button wiring.
/// Subscribes to TourManager.OnTourChanged to drive canvas visibility
/// </summary>
public class MainMenuController : MonoBehaviour
{
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

    [Header("Side Buttons")]

    [Tooltip("Top side-button. Opens the ListViewPanel.")]
    [SerializeField] private Button topSideButton;

    [Tooltip("Middle side-button. Placeholder - no-op in Session B.")]
    [SerializeField] private Button middleSideButton;

    [Tooltip("Bottom side-button. Placeholder - no-op in Session B.")]
    [SerializeField] private Button bottomSideButton;

    [Header("Sub-Panels")]

    [Tooltip("ListViewPanel opened by the top side-button.")]
    [SerializeField] private ListViewPanel listViewPanel;

    [Header("Confirmation Dialog")]

    [Tooltip("Shared ConfirmationDialog used for the return-to-menu flow.")]
    [SerializeField] private ConfirmationDialog confirmationDialog;

    [Header("Messages")]

    [Tooltip("Message shown in the confirmation dialog when the user clicks Return to Menu.")]
    [SerializeField] private string returnConfirmationMessage = "Return to the main menu?";

    #endregion

    private bool _isReady;

    private void Awake()
    {        
        _isReady = ValidateReferences();  
    }

    private void Start()
    {
        if (!_isReady)
            return;

        // Wire button handlers
        topSideButton.onClick.AddListener(HandleTopSideButtonClicked);
        middleSideButton.onClick.AddListener(HandleMiddleSideButtonClicked);
        bottomSideButton.onClick.AddListener(HandleBottomSideButtonClicked);
        returnToMenuButton.onClick.AddListener(HandleReturnToMenuClicked);

        // Title text is sourced from the catalog
        titleText.text = tourCatalog.menuTitle;

        // Assign list panel close button callback
        listViewPanel.SetCloseCallback(CloseListPanel);

        // Apply initial visibility from current state.
        ApplyCanvasVisibility(tourManager.CurrentTour);

        // Subscribe to tour state changes.
        tourManager.OnTourChanged += HandleTourChanged;
    }

    private void OnDestroy()
    {
        if (tourManager != null)
            tourManager.OnTourChanged -= HandleTourChanged;
    }

    #region Handlers

    private void HandleTourChanged(TourData tour)
    {
        ApplyCanvasVisibility(tour);
    }

    private void HandleTopSideButtonClicked()
    {
        listViewPanel.Open();
    }

    private void HandleMiddleSideButtonClicked()
    {
        // TODO
    }

    private void HandleBottomSideButtonClicked()
    {
        // TODO
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

    #region Private Methods

    /// <summary>
    /// Callback invoked by ListViewPanel when its close button is clicked
    /// </summary>
    private void CloseListPanel()
    {
        listViewPanel.Close();
    }

    private void ApplyCanvasVisibility(TourData tour)
    {
        bool tourLoaded = tour != null;

        menuCanvas.SetActive(!tourLoaded);
        tourOverlayCanvas.SetActive(tourLoaded);

        listViewPanel.Close();
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

        if (topSideButton == null)
        {
            Debug.LogError("[MainMenuController] TopSideButton reference is not assigned.");
            valid = false;
        }

        if (middleSideButton == null)
        {
            Debug.LogError("[MainMenuController] MiddleSideButton reference is not assigned.");
            valid = false;
        }

        if (bottomSideButton == null)
        {
            Debug.LogError("[MainMenuController] BottomSideButton reference is not assigned.");
            valid = false;
        }

        if (listViewPanel == null)
        {
            Debug.LogError("[MainMenuController] ListViewPanel reference is not assigned.");
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
