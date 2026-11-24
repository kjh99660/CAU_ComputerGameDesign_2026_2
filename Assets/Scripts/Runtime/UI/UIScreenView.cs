using TMPro;
using UnityEngine;
using UnityEngine.UI;

public abstract class UIScreenView : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;

    public bool IsVisible => gameObject.activeSelf;

    public virtual void AutoBind()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
    }

    // This game has no keyboard or gamepad menu navigation: buttons are clicked, and Esc, Space and Enter are handled
    // by UiLayer. With navigation on, a clicked button stays the EventSystem's selected object, and Enter (also the
    // UI's Submit key) presses it again later: START twice, a dialogue line skipped, RETRY before the result has
    // counted up. With navigation off a click selects nothing. UIRoot applies this to every button of every screen
    // once; a view that creates a button at run time applies it to that button.
    public static void TurnNavigationOff(Selectable selectable)
    {
        if (selectable == null) return;
        Navigation navigation = selectable.navigation;
        if (navigation.mode == Navigation.Mode.None) return;
        navigation.mode = Navigation.Mode.None;
        selectable.navigation = navigation;
    }

    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible)
            gameObject.SetActive(visible);
    }

    public void SetInputEnabled(bool enabled)
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            return;

        canvasGroup.interactable = enabled;
        canvasGroup.blocksRaycasts = enabled;
    }

    public virtual void ResetView() { }

    protected T FindNamed<T>(string objectName) where T : Component
    {
        Transform found = FindNamedTransform(transform, objectName);
        return found != null ? found.GetComponent<T>() : null;
    }

    protected static void SetText(TMP_Text target, string value)
    {
        if (target != null)
            target.text = value ?? string.Empty;
    }

    protected static void SetButtonInteractable(Button target, bool interactable)
    {
        if (target != null)
            target.interactable = interactable;
    }

    // A keyboard press of a screen's button: goes through the button's own onClick (listeners, click sound) and only
    // when the mouse could click it too (active, interactable, its screen accepting input).
    protected static bool Press(Button target)
    {
        if (target == null || !target.isActiveAndEnabled || !target.IsInteractable())
            return false;
        target.onClick.Invoke();
        return true;
    }

    private static Transform FindNamedTransform(Transform parent, string objectName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == objectName)
                return child;
            Transform nested = FindNamedTransform(child, objectName);
            if (nested != null)
                return nested;
        }
        return null;
    }
}
