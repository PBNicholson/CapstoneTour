using System;
using UnityEngine;

/// <summary>
/// Base class for a panel shown in the main menu's sub-panel area (tour list, campus map, ...).
///
/// MainMenuController owns which sub-panel is open: it calls Open() and Close(), and it
/// hides every registered sub-panel when the scene starts. A sub-panel therefore must NOT
/// deactivate itself in Awake, and it does not matter whether its GameObject is saved
/// active or inactive in the scene.
///
/// To add a sub-panel: derive from this class, put the component on the panel's root
/// GameObject under SubPanelContainer, and assign it to one of the Sub Panels entries on
/// MainMenuController. No change to MainMenuController is needed.
/// </summary>
public abstract class MenuSubPanel : MonoBehaviour
{
    /// <summary>
    /// Raised when the panel asks to be closed, typically from its own close button.
    /// MainMenuController subscribes and performs the close.
    /// </summary>
    public event Action CloseRequested;

    /// <summary>
    /// True while the panel's GameObject is active.
    /// </summary>
    public bool IsOpen => gameObject.activeSelf;

    /// <summary>
    /// Shows the panel. OnOpening() runs first, while the panel is still hidden.
    /// </summary>
    public void Open()
    {
        OnOpening();
        gameObject.SetActive(true);
    }

    /// <summary>
    /// Hides the panel.
    /// </summary>
    public void Close()
    {
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Called by Open() just before the panel is activated. Override to reset state.
    /// On the first Open() of a panel that was saved inactive, this runs before Awake,
    /// so it must not depend on anything Awake sets up.
    /// </summary>
    protected virtual void OnOpening()
    {
    }

    /// <summary>
    /// Call from the derived panel's close button handler.
    /// </summary>
    protected void RequestClose()
    {
        CloseRequested?.Invoke();
    }
}
