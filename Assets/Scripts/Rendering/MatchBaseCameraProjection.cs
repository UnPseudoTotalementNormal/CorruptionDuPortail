using UnityEngine;

namespace Game.Rendering
{
    /// <summary>
    /// Copie la projection de la caméra de base (pilotée par CinemachineBrain) sur cette
    /// caméra overlay, frame par frame. Le transform est hérité via le parenting sous la
    /// base. Permet à AboveBlurCamera de rendre des objets World Space parfaitement alignés
    /// avec ce que voit la base, malgré les changements de FOV/lentille de Cinemachine.
    /// </summary>
    [DefaultExecutionOrder(10000)] // après CinemachineBrain (LateUpdate)
    [RequireComponent(typeof(Camera))]
    public class MatchBaseCameraProjection : MonoBehaviour
    {
        [SerializeField] private Camera baseCamera;

        private Camera cam;

        private void Awake() => cam = GetComponent<Camera>();

        private void LateUpdate()
        {
            if (!baseCamera) return;
            cam.projectionMatrix = baseCamera.projectionMatrix;
        }
    }
}
