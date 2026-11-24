using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class StartView : UIScreenView
{
    [SerializeField] private Button startButton;
    // The best score so far, under the title. Optional: a prefab without the row only lacks it. Hidden until there is one.
    [SerializeField] private GameObject bestScoreRow;
    [SerializeField] private TMP_Text bestScoreValue;
    private int _shownBest = -1;

    public event Action StartClicked;

    public override void AutoBind()
    {
        base.AutoBind();
        if (startButton == null) startButton = FindNamed<Button>("StartButton");
        if (bestScoreRow == null)
        {
            RectTransform row = FindNamed<RectTransform>("BestScoreRow");
            if (row != null) bestScoreRow = row.gameObject;
        }
        if (bestScoreValue == null) bestScoreValue = FindNamed<TMP_Text>("BestScoreValue");
    }

    private void OnEnable()
    {
        AutoBind();
        if (startButton != null) startButton.onClick.AddListener(HandleStartClicked);
    }

    private void OnDisable()
    {
        if (startButton != null) startButton.onClick.RemoveListener(HandleStartClicked);
    }

    public void SetStartInteractable(bool interactable) => SetButtonInteractable(startButton, interactable);

    // 0 = no round has been scored yet: the row stays hidden. The same six digits as the result screen.
    public void RenderBest(int best)
    {
        if (bestScoreRow == null) return;
        bool show = best > 0;
        if (bestScoreRow.activeSelf != show) bestScoreRow.SetActive(show);
        if (!show || bestScoreValue == null || best == _shownBest) return;
        _shownBest = best;
        bestScoreValue.SetText("{0:000000}", best);
    }

    // Space/Enter on the start screen: the same click the mouse makes, refused while the button cannot be clicked.
    public bool PressStart() => Press(startButton);

    private void HandleStartClicked()
    {
        StartClicked?.Invoke();
    }
}
