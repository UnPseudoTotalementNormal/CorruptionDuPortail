using UI.InfoTable;
using UnityEngine;

namespace UI.Spike
{
    /// <summary>
    /// HARNESS-ONLY debug panel for the TabletOnlySpike scene: draws IMGUI buttons to grow/shrink the demo
    /// deduction grid live (add/remove players and roles) so the UITK InfoTable can be exercised at different
    /// sizes without a running game. Pure OnGUI — no Canvas, no scene wiring beyond the source reference.
    /// Throwaway; never used in the real game.
    /// </summary>
    public class SpikeInfoTableControls : MonoBehaviour
    {
        [SerializeField] private DemoInfoTableDataSource _source;

        private GUIStyle _btn;

        private void OnGUI()
        {
            if (_source == null) return;

            if (_btn == null)
            {
                _btn = new GUIStyle(GUI.skin.button) { fontSize = 20, fixedHeight = 44 };
            }

            GUILayout.BeginArea(new Rect(16, 16, 260, 400), GUI.skin.box);
            GUILayout.Label($"<b>InfoTable — Test</b>\nJoueurs: {_source.PlayerCount}   Rôles: {_source.RoleCount}",
                new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true });

            GUILayout.Space(6);
            if (GUILayout.Button("+ Joueur", _btn)) _source.AddPlayer();
            if (GUILayout.Button("− Joueur", _btn)) _source.RemovePlayer();

            GUILayout.Space(6);
            if (GUILayout.Button("+ Rôle", _btn)) _source.AddRole();
            if (GUILayout.Button("− Rôle", _btn)) _source.RemoveRole();

            GUILayout.EndArea();
        }
    }
}
