using UnityEngine;

// UIRoot.Configure가 한 번 부른다. UI Prefab을 고치지 않고 실행 중에 두 가지를 붙인다.
//  - 각 화면 View(UIRoot의 직계 자식 UIScreenView)에 SafeAreaFitter: 내용은 안전 영역 안, 배경·어둡게 덮는 막은 화면 전체.
//  - UIRoot에 ScreenTransition: 화면 전체를 덮는 TransitionLayer 페이드.
// 다시 불러도 이미 붙은 구성요소를 그대로 쓰고 새로 만들지 않는다. Scene에 미리 붙여 둔 ScreenTransition이 있으면 그 설정을 쓴다.
public static class UiScreenLayers
{
    public static ScreenTransition Install(UIRoot root, GameStateManager stateManager)
    {
        if (root == null)
            return null;
        Transform canvas = root.transform;
        for (int i = 0; i < canvas.childCount; i++)
        {
            Transform child = canvas.GetChild(i);
            if (child.TryGetComponent(out UIScreenView _) && !child.TryGetComponent(out SafeAreaFitter _))
                child.gameObject.AddComponent<SafeAreaFitter>();
        }
        if (!root.TryGetComponent(out ScreenTransition transition))
            transition = root.gameObject.AddComponent<ScreenTransition>();
        transition.Configure(stateManager);
        return transition;
    }
}
