#region

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#endregion

namespace UI
{
    /// <summary>
    /// Applique le matériau glitch (Custom/UI_GlitchVideo) à tous les Graphics uGUI sous cet
    /// objet, pour que toute la carte glitche d'un bloc. Les bandes du shader sont calculées
    /// en espace écran, donc les éléments se déchirent aux mêmes hauteurs, de façon synchrone.
    /// Les textes TMP sont ignorés : leur shader SDF ne peut pas être remplacé par un shader sprite.
    /// </summary>
    public class UIGlitchGroup : MonoBehaviour
    {
        private static readonly int GlitchIntensityId = Shader.PropertyToID("_GlitchIntensity");

        [SerializeField] private Material glitchMaterial;
        [SerializeField] [Range(0f, 1f)] private float intensity = 1f;
        [SerializeField] private bool applyOnEnable;

        [Tooltip("Sous-arbres exclus du glitch : le Graphic ciblé ET ses descendants gardent leur " +
                 "matériau d'origine (ex. la bordure de carte, le canvas de vote).")]
        [SerializeField] private List<Transform> excludedRoots = new();

        private readonly Dictionary<Graphic, Material> originalMaterials = new();
        private Material runtimeMaterial;

        public bool IsApplied { get; private set; }

        /// <summary>Intensité globale du glitch (0 = aucun effet), tweenable pour monter/descendre l'effet.</summary>
        public float Intensity
        {
            get => intensity;
            set
            {
                intensity = Mathf.Clamp01(value);
                if (runtimeMaterial != null)
                {
                    runtimeMaterial.SetFloat(GlitchIntensityId, intensity);
                }
            }
        }

        private void OnEnable()
        {
            if (applyOnEnable)
            {
                Apply();
            }
        }

        private void OnDisable()
        {
            Remove();
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
            {
                Destroy(runtimeMaterial);
            }
        }

        /// <summary>Active le glitch sur tous les Graphics enfants (TMP exclus).</summary>
        public void Apply()
        {
            if (IsApplied || glitchMaterial == null)
            {
                return;
            }

            // Instance clonée : ne jamais muter l'asset .mat partagé.
            if (runtimeMaterial == null)
            {
                runtimeMaterial = new Material(glitchMaterial);
            }

            runtimeMaterial.SetFloat(GlitchIntensityId, intensity);

            foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
            {
                if (graphic is TMP_Text)
                {
                    continue;
                }

                if (IsExcluded(graphic.transform))
                {
                    continue;
                }

                originalMaterials[graphic] = graphic.material;
                graphic.material = runtimeMaterial;
            }

            IsApplied = true;
        }

        /// <summary>Coupe le glitch et restaure les matériaux d'origine.</summary>
        public void Remove()
        {
            if (!IsApplied)
            {
                return;
            }

            foreach (KeyValuePair<Graphic, Material> entry in originalMaterials)
            {
                if (entry.Key != null)
                {
                    entry.Key.material = entry.Value;
                }
            }

            originalMaterials.Clear();
            IsApplied = false;
        }

        /// <summary>True si <paramref name="target"/> est l'un des roots exclus ou un de leurs descendants.</summary>
        private bool IsExcluded(Transform target)
        {
            foreach (Transform excluded in excludedRoots)
            {
                if (excluded != null && target.IsChildOf(excluded))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
