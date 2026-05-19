using Unity.VisualScripting;
using UnityEngine;

namespace TransformComposition
{
    public static class ComponentCompositorExtension
    {
        /// <summary>
        /// Extension method to easily get or add a TransformCompositorComponent to any Component.
        /// </summary>
        public static TransformCompositorComponent GetTransformCompositor(this Component component)
        {
            return component.gameObject.GetComponent<TransformCompositorComponent>();
        }

        /// <summary>
        /// Extension method to easily get or add a TransformCompositorComponent to any GameObject.
        /// </summary>
        public static TransformCompositorComponent GetTransformCompositor(this GameObject gameObject)
        {
            if (gameObject.TryGetComponent(out TransformCompositorComponent transformCompositorComponent))
            {
                return transformCompositorComponent;
            }
            return gameObject.AddComponent<TransformCompositorComponent>();
        }
    }
}