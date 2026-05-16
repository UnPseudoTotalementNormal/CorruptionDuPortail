using Extensions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.Editor
{
    public class GameObjectExtensionTests
    {
        [Test]
        public void SetLayerRecursively_Int_SetsLayerForAllChildren()
        {
            GameObject parent = new GameObject("Parent");
            GameObject child = new GameObject("Child");
            GameObject grandchild = new GameObject("Grandchild");

            child.transform.SetParent(parent.transform);
            grandchild.transform.SetParent(child.transform);

            int targetLayer = 5; // Usually UI
            parent.SetLayerRecursively(targetLayer);

            Assert.AreEqual(targetLayer, parent.layer);
            Assert.AreEqual(targetLayer, child.layer);
            Assert.AreEqual(targetLayer, grandchild.layer);

            Object.DestroyImmediate(parent);
        }
        
        [Test]
        public void SetLayerRecursively_String_SetsLayerForAllChildren()
        {
            GameObject parent = new GameObject("Parent");
            GameObject child = new GameObject("Child");

            child.transform.SetParent(parent.transform);

            string targetLayerName = "UI"; 
            int expectedLayer = LayerMask.NameToLayer(targetLayerName);
            
            parent.SetLayerRecursively(targetLayerName);

            Assert.AreEqual(expectedLayer, parent.layer);
            Assert.AreEqual(expectedLayer, child.layer);

            Object.DestroyImmediate(parent);
        }
        
        [Test]
        public void SetLayerRecursively_String_InvalidLayer_OutputsWarningAndDoesNotChangeLayer()
        {
            GameObject parent = new GameObject("Parent");
            int originalLayer = parent.layer;
            
            LogAssert.Expect(LogType.Warning, "Layer 'InvalidLayerThatDoesNotExist' does not exist.");
            
            parent.SetLayerRecursively("InvalidLayerThatDoesNotExist");
            
            Assert.AreEqual(originalLayer, parent.layer);

            Object.DestroyImmediate(parent);
        }
    }
}
