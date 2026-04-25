using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Generic, reusable modal confirmation dialog.
/// </summary>
public class ConfirmationDialog : MonoBehaviour
{
    #region Serialized Fields

    [Header("UI References")]

    [Tooltip("Text element displaying the dialog message.")]
    [SerializeField] private TextMeshProUGUI messageText;

    [Tooltip("Button the user clicks to confirm the action")]
    [SerializeField] private Button confirmButton;

    [Tooltip("Button the user clicks to cancel the action.")]
    [SerializeField] private Button cancelButton;

    #endregion

    // Callbacks
    private Action _onConfirm;
    private Action _onCancel;

    private void Awake()
    {
        ValidateReferences();

        if (confirmButton != null)
            confirmButton.onClick.AddListener(HandleConfirmClicked);
        if (cancelButton != null)
            cancelButton.onClick.AddListener(HandleCancelClicked);
        // Start hidden
        gameObject.SetActive(false);
    }

    #region Public API

    /// <summary>
    /// Displays the dialog with the given message and callbacks.
    /// </summary>
    /// <param name="message">Message text shown in the dialog body.</param>
    /// <param name="onConfirm">Invoked when the user clicks Confirm. Required.</param>
    /// <param name="onCancel">Invoked when the user clicks Cancel. Optional - pass null if no cancel-side action is needed.</param>
    public void Show(string message, Action onConfirm, Action onCancel = null)
    {
        _onConfirm = onConfirm;
        _onCancel = onCancel;

        if (messageText != null)
            messageText.text = message;

        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);

        _onConfirm = null;
        _onCancel = null;
    }

    #endregion

    #region Button Handlers

    private void HandleConfirmClicked()
    {
        Action callback = _onConfirm;
        Hide();
        callback?.Invoke();
    }

    private void HandleCancelClicked()
    {
        Action callback = _onCancel;
        Hide();
        callback?.Invoke();
    }

    #endregion

    #region Validation

    private void ValidateReferences()
    {
        if (messageText == null)
            Debug.LogError("[ConfirmationDialog] MessageText reference is not assigned.");
        if (confirmButton == null)
            Debug.LogError("[ConfirmationDialog] ConfirmButton reference is not assigned.");
        if (cancelButton == null)
            Debug.LogError("[ConfirmationDialog] CancelButton reference is not assigned.");
    }

    #endregion
}
