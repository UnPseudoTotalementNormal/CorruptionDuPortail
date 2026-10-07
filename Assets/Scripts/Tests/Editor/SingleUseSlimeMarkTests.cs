using System.Collections.Generic;
using System.Linq;
using Board.UI.PowerBar;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// Board T2: a one-shot copied power's 3D object gets the slime coat + drips (SingleUseSlimeMark), once, and a
    /// missing asset only skips its own part.
    /// </summary>
    public class SingleUseSlimeMarkTests
    {
        private readonly List<Object> created = new();
        private Material baseMaterial;
        private Material coat;
        private GameObject dripPrefab;

        [SetUp]
        public void SetUp()
        {
            Shader _shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Hidden/InternalErrorShader");
            baseMaterial = Track(new Material(_shader) { name = "Base" });
            coat = Track(new Material(_shader) { name = "Coat" });
            dripPrefab = Track(new GameObject("DripTemplate"));
            dripPrefab.AddComponent<ParticleSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object _object in created.Where(_o => _o))
            {
                Object.DestroyImmediate(_object);
            }
            created.Clear();
        }

        [Test]
        public void Apply_CoatsEveryMeshRendererAndAddsOneDripEmitterOnTheModelMesh()
        {
            Transform _visual = BuildVisual(out MeshRenderer _renderer);

            SingleUseSlimeMark.Apply(_visual, coat, dripPrefab);

            CollectionAssert.AreEqual(new[] { baseMaterial, coat }, _renderer.sharedMaterials);
            Transform _drips = _visual.Find(SingleUseSlimeMark.DRIPS_OBJECT_NAME);
            Assert.IsNotNull(_drips, "drip emitter missing");
            ParticleSystem.ShapeModule _shape = _drips.GetComponent<ParticleSystem>().shape;
            Assert.AreEqual(ParticleSystemShapeType.MeshRenderer, _shape.shapeType);
            Assert.AreSame(_renderer, _shape.meshRenderer);
        }

        [Test]
        public void Apply_Twice_NeverStacksASecondCoatOrEmitter()
        {
            Transform _visual = BuildVisual(out MeshRenderer _renderer);

            SingleUseSlimeMark.Apply(_visual, coat, dripPrefab);
            SingleUseSlimeMark.Apply(_visual, coat, dripPrefab);

            CollectionAssert.AreEqual(new[] { baseMaterial, coat }, _renderer.sharedMaterials);
            Assert.AreEqual(1, CountDrips(_visual));
        }

        [Test]
        public void Apply_MissingAssets_SkipsOnlyTheMissingPart()
        {
            Transform _noCoat = BuildVisual(out MeshRenderer _noCoatRenderer);
            Transform _noDrips = BuildVisual(out MeshRenderer _noDripsRenderer);

            SingleUseSlimeMark.Apply(_noCoat, null, dripPrefab);
            SingleUseSlimeMark.Apply(_noDrips, coat, null);
            Assert.DoesNotThrow(() => SingleUseSlimeMark.Apply(null, coat, dripPrefab));

            CollectionAssert.AreEqual(new[] { baseMaterial }, _noCoatRenderer.sharedMaterials);
            Assert.AreEqual(1, CountDrips(_noCoat));
            CollectionAssert.AreEqual(new[] { baseMaterial, coat }, _noDripsRenderer.sharedMaterials);
            Assert.AreEqual(0, CountDrips(_noDrips));
        }

        [Test]
        public void Apply_NonReadableMesh_DripsFallFromTheModelBoundsInstead()
        {
            // Imported power models have Read/Write off: a MeshRenderer shape would emit nothing in a player build.
            Transform _visual = BuildVisual(out MeshRenderer _renderer);
            Mesh _locked = Track(Object.Instantiate(_renderer.GetComponent<MeshFilter>().sharedMesh));
            _locked.UploadMeshData(true);
            _renderer.GetComponent<MeshFilter>().sharedMesh = _locked;
            _renderer.transform.localPosition = new Vector3(0f, 2f, 0f);

            SingleUseSlimeMark.Apply(_visual, coat, dripPrefab);

            ParticleSystem.ShapeModule _shape = _visual.Find(SingleUseSlimeMark.DRIPS_OBJECT_NAME).GetComponent<ParticleSystem>().shape;
            Assert.AreEqual(ParticleSystemShapeType.Box, _shape.shapeType);
            Assert.That(Vector3.Distance(new Vector3(0f, 2f, 0f), _shape.position), Is.LessThan(0.001f));
            Assert.That(Vector3.Distance(Vector3.one, _shape.scale), Is.LessThan(0.001f));
        }

        [Test]
        public void Apply_LeavesTextAndInactiveMeshesUncoated()
        {
            Transform _visual = BuildVisual(out MeshRenderer _renderer);
            GameObject _label = new GameObject("Label", typeof(TextMeshPro));
            _label.transform.SetParent(_visual, false);
            MeshRenderer _labelRenderer = _label.GetComponent<MeshRenderer>();
            Material[] _labelMaterials = _labelRenderer.sharedMaterials;
            GameObject _hidden = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _hidden.transform.SetParent(_visual, false);
            _hidden.SetActive(false);
            _hidden.GetComponent<MeshRenderer>().sharedMaterials = new[] { baseMaterial };

            SingleUseSlimeMark.Apply(_visual, coat, dripPrefab);

            CollectionAssert.AreEqual(new[] { baseMaterial, coat }, _renderer.sharedMaterials);
            CollectionAssert.AreEqual(_labelMaterials, _labelRenderer.sharedMaterials);
            CollectionAssert.AreEqual(new[] { baseMaterial }, _hidden.GetComponent<MeshRenderer>().sharedMaterials);
        }

        [Test]
        public void ReleaseDrips_DetachesTheEmitterSoFallingDropsOutliveTheObject()
        {
            Transform _visual = BuildVisual(out _);
            SingleUseSlimeMark.Apply(_visual, coat, dripPrefab);
            Transform _drips = _visual.Find(SingleUseSlimeMark.DRIPS_OBJECT_NAME);
            Track(_drips.gameObject);

            SingleUseSlimeMark.ReleaseDrips(_visual);

            Assert.IsNull(_drips.parent, "emitter still under the leaving object");
            ParticleSystem _particles = _drips.GetComponent<ParticleSystem>();
            Assert.IsFalse(_particles.isEmitting);
            Assert.AreEqual(ParticleSystemStopAction.Destroy, _particles.main.stopAction);
            Assert.DoesNotThrow(() => SingleUseSlimeMark.ReleaseDrips(_visual));
        }

        private Transform BuildVisual(out MeshRenderer _renderer)
        {
            GameObject _root = Track(new GameObject("Visual"));
            GameObject _model = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _model.transform.SetParent(_root.transform, false);
            _renderer = _model.GetComponent<MeshRenderer>();
            _renderer.sharedMaterials = new[] { baseMaterial };
            return _root.transform;
        }

        private static int CountDrips(Transform _visual)
        {
            return _visual.Cast<Transform>().Count(_child => _child.name == SingleUseSlimeMark.DRIPS_OBJECT_NAME);
        }

        private T Track<T>(T _object) where T : Object
        {
            created.Add(_object);
            return _object;
        }
    }
}
