using UnityEngine;

public class SwordEventRelay : MonoBehaviour
{
    public SwordFollowBlend sword;
    public void Draw() { sword.Draw(); }
    public void Sheathe() { sword.Sheathe(); }
}