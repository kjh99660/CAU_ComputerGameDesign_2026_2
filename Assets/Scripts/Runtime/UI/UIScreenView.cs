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
