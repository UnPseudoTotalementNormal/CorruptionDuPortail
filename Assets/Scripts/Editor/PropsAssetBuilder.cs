using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Board.UI.PowerBar.EditorTools
{
    /// <summary>
    /// Rebuilds the artist's props from the exported art (tools/art/props/README.md; run again after a new export): the
    /// URP materials (values of the .blend's nodes, read one by one), the FBX remaps and texture settings, and one prefab
    /// per prop. A power object sits on the table's power bar: its prefab root keeps an identity scale, because
    /// PowerBarObject3D resets it and clones it (visual + collider), and the size, the tilt towards the player and the
    /// collider live under it.
    /// </summary>
    public static class PropsAssetBuilder
    {
        private const string ModelsDir = "Assets/Art/Models/Props";
        private const string TexturesDir = "Assets/Art/Textures/Props";
        private const string MaterialsDir = "Assets/Art/Materials/Props";
        private const string PowerPrefabsDir = "Assets/Prefabs/PowersBar/3DModels";
        private const string PropPrefabsDir = "Assets/Prefabs/Props";
        private const int BoardClickableLayer = 7; // 3DPhysical: the only layer the reticle and CameraBrain's raycaster see.

        private sealed class Look
        {
            public string Texture;
            public Color Colour = Color.white;
            public float Metallic;
            public float Roughness = 0.5f;
            public Color Emission = Color.black;
            public bool EmissionFromTexture;
            public float Alpha = 1f;
            public bool AlphaFromTexture; // blended by the base map's alpha (the board's translucent slime)
            // Prop_<Texture>_Normal / _Gloss / _Emission are picked up when tools/art/props/prepare_textures.py made them.
        }

        // Blender material name -> look. Same names as the FBX's material slots. The game has no reflections
        // (RenderSettings.reflectionIntensity 0): the file's metallic values render almost black, so they are capped by
        // MetallicCap, the metal's shine coming from the lights alone.
        private const float MetallicCap = 0.25f;

        private static readonly Dictionary<string, Look> Looks = new Dictionary<string, Look>
        {
            ["Arcane Cube"] = new Look { Texture = "Prop_ArcaneCube" },
            ["Arcane Cube Emission"] = new Look { Texture = "Prop_ArcaneCube", EmissionFromTexture = true, Emission = Color.white * 17.2f },
            ["Beacon"] = new Look { Texture = "Prop_Beacon", Roughness = 0.276f, Emission = Color.white * 4.2f },
            ["Bell"] = new Look { Texture = "Prop_Bell", Metallic = 0.717f, Roughness = 0.393f },
            ["Leaf Heart"] = new Look { Colour = new Color(0.1f, 0.05f, 0.03f), Emission = new Color(1f, 0.42f, 0.173f) * 3.08f },
            ["Button Eyes"] = new Look { Colour = new Color(0.029f, 0.025f, 0.026f), Roughness = 0.4f },
            ["Candle Bit"] = new Look { Colour = new Color(0.003f, 0.003f, 0.003f), Roughness = 0.788f },
            ["Candle Plate"] = new Look { Colour = new Color(0.356f, 0.222f, 0.142f), Metallic = 0.82f, Roughness = 0.413f },
            ["Chain Pile"] = new Look { Texture = "Prop_ChainPile", Metallic = 0.509f, Roughness = 0.402f },
            ["Single Chain"] = new Look { Texture = "Prop_ChainLink", Metallic = 0.509f, Roughness = 0.402f },
            ["Chair"] = new Look { Texture = "Prop_Chair", Roughness = 0.7f },
            ["Corruptor"] = new Look { Texture = "Prop_Corruptor" },
            ["Crystal Ball"] = new Look { Colour = new Color(0.9f, 0.86f, 0.95f), Roughness = 0.04f, Alpha = 0.16f },
            ["Crystal Ball Base"] = new Look { Texture = "Prop_CrystalBallBase", Metallic = 0.84f, Roughness = 0.374f },
            ["Crystal Skull"] = new Look { Texture = "Prop_CrystalSkull", Roughness = 0.532f },
            // Leaves.jpg and LeafB2.jpg (the heart's leaf cards) are not on the Drive: flat gold until the artist sends them.
            ["Leaves"] = new Look { Colour = new Color(0.72f, 0.47f, 0.13f), Metallic = 0.6f, Roughness = 0.45f },
            ["Leaves 2"] = new Look { Colour = new Color(0.55f, 0.33f, 0.09f), Metallic = 0.6f, Roughness = 0.5f },
            ["Purple Hand"] = new Look { Texture = "Prop_PurpleHand" },
            ["Robot"] = new Look { Texture = "Prop_BeaconRobot", Emission = Color.white * 27f },
            ["Sniper"] = new Look { Texture = "Prop_Sniper", Roughness = 0.361f },
            ["Table"] = new Look { Texture = "Prop_Table", Roughness = 0.724f },
            ["Teddy Bear"] = new Look { Texture = "Prop_TeddyBear" },
            ["Timer"] = new Look { Texture = "Prop_Timer", Roughness = 0.363f },
            ["Timer Glass"] = new Look { Colour = new Color(0.8f, 0.8f, 0.8f), Alpha = 0.23f },
            ["Totem"] = new Look { Texture = "Prop_Totem", Emission = Color.white * 14.8f },
            ["Wooden Board"] = new Look { Texture = "Prop_WoodenBoard", AlphaFromTexture = true },
        };

        private sealed class PowerObject
        {
            public string Model;
            public float Size; // longest side on the table, world units (BasePower = 2)
            public Vector3 Euler; // tilt towards the night's camera: above the table's middle, so at the bar it looks from the far side
        }

        private static readonly Dictionary<string, PowerObject> PowerObjects = new Dictionary<string, PowerObject>
        {
            ["PowerObject_ArcaneCube"] = new PowerObject { Model = "ArcaneCube", Size = 2.2f, Euler = new Vector3(0f, 25f, 0f) },
            ["PowerObject_Bell"] = new PowerObject { Model = "Bell", Size = 2f, Euler = new Vector3(95f, 0f, 0f) },
            ["PowerObject_ChainPile"] = new PowerObject { Model = "ChainPile", Size = 2.6f, Euler = new Vector3(0f, 0f, 0f) },
            ["PowerObject_Corruptor"] = new PowerObject { Model = "Corruptor", Size = 2.2f, Euler = new Vector3(108f, 0f, 0f) },
            ["PowerObject_CrystalBall"] = new PowerObject { Model = "CrystalBall", Size = 2.2f, Euler = new Vector3(105f, 0f, 0f) },
            ["PowerObject_CrystalSkull"] = new PowerObject { Model = "CrystalSkull", Size = 2.1f, Euler = new Vector3(102f, 0f, 0f) },
            ["PowerObject_LeafHeart"] = new PowerObject { Model = "LeafHeart", Size = 2.3f, Euler = new Vector3(105f, 0f, 0f) },
            ["PowerObject_PurpleHand"] = new PowerObject { Model = "PurpleHand", Size = 2.3f, Euler = new Vector3(105f, 0f, 0f) },
            ["PowerObject_Sniper"] = new PowerObject { Model = "Sniper", Size = 3f, Euler = new Vector3(70f, 25f, 0f) },
            ["PowerObject_TeddyBear"] = new PowerObject { Model = "TeddyBear", Size = 2.1f, Euler = new Vector3(108f, 0f, 0f) },
            ["PowerObject_WoodenBoard"] = new PowerObject { Model = "WoodenBoard", Size = 2.8f, Euler = new Vector3(0f, 0f, 0f) },
            ["PowerObject_BeaconRobot"] = new PowerObject { Model = "BeaconRobot", Size = 2.3f, Euler = new Vector3(105f, 0f, 0f) },
            ["PowerObject_Totem"] = new PowerObject { Model = "Totem", Size = 2.4f, Euler = new Vector3(105f, 0f, 0f) },
        };

        // Who gets which object: the asset tracker of the board task T33 (Corrompu_Suivi_Assets_3D.xlsx, one row per role's
        // power-up). Not in it, kept from the mockups objets-alex v1: the Oracle's ball and the Jailer's chains. The tracker's
        // Doorman bell pairs with the Messenger's animation row. Luma's card is not delivered: the default model.
        // Ugës's wooden board holds his copied powers: not a power's object.
        private static readonly Dictionary<string, string> PowerAssignments = new Dictionary<string, string>
        {
            ["TargetedCorruption"] = "PowerObject_Corruptor", // Occult Mage n°1
            ["EmbraceOfShadows"] = "PowerObject_ArcaneCube", // Occult Mage n°2
            ["CorruptingMark"] = "PowerObject_Corruptor",
            ["ChainedByTheShadows"] = "PowerObject_ChainPile", // Abyss
            ["TruthChains"] = "PowerObject_ChainPile",
            ["Omniscience"] = "PowerObject_Totem", // Robot
            ["DroolyHealing"] = "PowerObject_CrystalBall", // Gloubi's magic ball
            ["Blessing"] = "PowerObject_LeafHeart", // Dryad
            ["PersonalBeacons"] = "PowerObject_BeaconRobot", // Technomancer
            ["LackOfAffection"] = "PowerObject_TeddyBear", // Orphan
            ["Reincarnation"] = "PowerObject_PurpleHand", // Incomplete
            ["BoundByInk"] = "PowerObject_Bell", // Doorman / Messenger
            ["CursedVision"] = "PowerObject_CrystalSkull", // Repentant
            ["ClandestineObservation"] = "PowerObject_Sniper", // Bounty Hunter
            ["HighPriorityBounty"] = "PowerObject_Sniper",
            ["VisionOfTheImpossible"] = "PowerObject_CrystalBall",
        };

        private const string WaxMaterialPath = "Assets/Art/Materials/Candle/CandleWax.mat";
        private const string PlayerAvatarPath = "Assets/Prefabs/Avatars/PlayerAvatar.prefab";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";

        // The chair at the artist's size next to a seated cat: its seat under the cat's feet, the backrest behind it. World
        // sizes of the other cats (root scale 3.375), in the avatar's local space.
        private const float OtherCatScale = 3.375f;
        private const float ChairWorldScale = 3.7f;
        private const float ChairBackWorld = 1.2f;
        private const float SeatHeightWorld = 10f;

        private static readonly string[] SceneProps = { "Table", "Chair", "Candle" };

        [MenuItem("Tools/Props/Rebuild assets")]
        public static void Rebuild()
        {
            Directory.CreateDirectory(MaterialsDir);
            Directory.CreateDirectory(PropPrefabsDir);
            AssetDatabase.Refresh();

            foreach (string _guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesDir }))
            {
                ConfigureTexture(AssetDatabase.GUIDToAssetPath(_guid));
            }

            var _materials = new Dictionary<string, Material>();
            foreach (KeyValuePair<string, Look> _look in Looks)
            {
                _materials[_look.Key] = BuildMaterial(_look.Key, _look.Value);
            }
            _materials["Candle Wax"] = AssetDatabase.LoadAssetAtPath<Material>(WaxMaterialPath);
            foreach (string _guid in AssetDatabase.FindAssets("t:Model", new[] { ModelsDir }))
            {
                ConfigureModel(AssetDatabase.GUIDToAssetPath(_guid), _materials);
            }

            foreach (KeyValuePair<string, PowerObject> _power in PowerObjects)
            {
                BuildPowerPrefab(_power.Key, _power.Value);
            }
            foreach (string _prop in SceneProps)
            {
                BuildPropPrefab(_prop);
            }
            AssetDatabase.SaveAssets();
            AssignPowerObjects();
            WirePlayerAvatar();
            AssetDatabase.SaveAssets();
        }

        private static void AssignPowerObjects()
        {
            foreach (KeyValuePair<string, string> _assignment in PowerAssignments)
            {
                string _path = $"Assets/Prefabs/Powers/{_assignment.Key}.prefab";
                GameObject _contents = PrefabUtility.LoadPrefabContents(_path);
                var _power = _contents.GetComponentInChildren<Characters.Powers.Power>(true);
                var _object = AssetDatabase.LoadAssetAtPath<GameObject>($"{PowerPrefabsDir}/{_assignment.Value}.prefab");
                if (!_power || !_object)
                {
                    Debug.LogError($"[Props] cannot assign {_assignment.Value} to {_assignment.Key}");
                }
                else if (_power.power3DObjectPrefab != _object)
                {
                    _power.power3DObjectPrefab = _object;
                    PrefabUtility.SaveAsPrefabAsset(_contents, _path);
                }
                PrefabUtility.UnloadPrefabContents(_contents);
            }
        }

        private static void WirePlayerAvatar()
        {
            GameObject _contents = PrefabUtility.LoadPrefabContents(PlayerAvatarPath);
            var _avatar = _contents.GetComponent<Avatars.PlayerAvatar>();
            var _so = new SerializedObject(_avatar);
            SerializedProperty _chairField = _so.FindProperty("_chair");
            if (!_chairField.objectReferenceValue)
            {
                var _chair = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{PropPrefabsDir}/Chair.prefab"), _contents.transform);
                _chair.name = "Chair";
                _chair.transform.localPosition = new Vector3(0f, -SeatHeightWorld / OtherCatScale, -ChairBackWorld / OtherCatScale);
                _chair.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                _chair.transform.localScale = Vector3.one * (ChairWorldScale / OtherCatScale);
                // In the cats' light: same layer and rendering layers as the cat's body.
                SkinnedMeshRenderer _body = null;
                foreach (SkinnedMeshRenderer _skin in _contents.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (_skin.name == "Body")
                    {
                        _body = _skin;
                    }
                }
                foreach (Renderer _renderer in _chair.GetComponentsInChildren<Renderer>(true))
                {
                    _renderer.gameObject.layer = _body.gameObject.layer;
                    _renderer.renderingLayerMask = _body.renderingLayerMask;
                }
                _chair.layer = _body.gameObject.layer;
                _chairField.objectReferenceValue = _chair;
            }
            _so.ApplyModifiedPropertiesWithoutUndo();

            var _chains = new SerializedObject(_contents.GetComponent<Presentation.Chains.CatChains>());
            _chains.FindProperty("_material").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsDir}/Prop_SingleChain.mat");
            _chains.FindProperty("_linkMesh").objectReferenceValue = MeshOf($"{ModelsDir}/ChainLink.fbx");
            _chains.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(_contents, PlayerAvatarPath);
            PrefabUtility.UnloadPrefabContents(_contents);
        }

        private static Mesh MeshOf(string _model)
        {
            foreach (Object _asset in AssetDatabase.LoadAllAssetsAtPath(_model))
            {
                if (_asset is Mesh _mesh)
                {
                    return _mesh;
                }
            }
            return null;
        }

        /// <summary>
        /// Puts the props in GameScene (objets-alex v1 T1, B1, C3, K2). Run once; a batchmode open recomputes layout sizes,
        /// so commit only the lines of the objects changed here (tools/art/cats/README.md, tools/art/props/merge_scene.py).
        /// </summary>
        [MenuItem("Tools/Props/Wire game scene")]
        public static void WireGameScene()
        {
            var _scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(GameScenePath);

            // ---- the table: over the board's surface, its legs down to the seated floor
            GameObject _old = SceneObject("Table");
            Transform _board = _old.transform.parent;
            var _table = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{PropPrefabsDir}/Table.prefab"), _board);
            _table.name = "ArtistTable";
            const float Top = -15.01f, Legs = 17f, Width = 22.84f, Depth = 15.7f, CentreZ = -0.79f;
            _table.transform.position = new Vector3(0f, Top - Legs, CentreZ);
            _table.transform.localScale = new Vector3(Width / 7.113f / _board.lossyScale.x, Legs / 3.546f / _board.lossyScale.y, Depth / 4.19f / _board.lossyScale.z);
            SetReference("Board.TableShapeSwitch", typeof(Board.TableShapeSwitch), "_rectangularTable", _table);
            var _day = new SerializedObject(Object.FindFirstObjectByType<Presentation.DayTableTransition>(FindObjectsInactive.Include));
            SerializedProperty _parts = _day.FindProperty("_tableParts");
            for (int _i = 0; _i < _parts.arraySize; _i++)
            {
                if (_parts.GetArrayElementAtIndex(_i).objectReferenceValue == _old)
                {
                    _parts.GetArrayElementAtIndex(_i).objectReferenceValue = _table;
                }
            }
            _day.ApplyModifiedPropertiesWithoutUndo();
            Object.DestroyImmediate(_old);
            // Its surface stays for the cards' collider and the candle's measures, not drawn.
            SceneObject("BoardModel").GetComponent<MeshRenderer>().enabled = false;

            // ---- the powers on the wood, no black band
            Transform _powersBar = SceneObject("PowersBar").transform;
            _powersBar.Find("Background").GetComponent<UnityEngine.UI.Image>().enabled = false;
            var _layout = (RectTransform)_powersBar.Find("Layout");
            _layout.anchoredPosition = new Vector2(_layout.anchoredPosition.x, PowerRowWorldShift / _layout.lossyScale.y);

            // ---- the floor under the chairs
            GameObject _floor = SceneObject("SeatedFloor");
            _floor.transform.position += Vector3.down * SeatHeightWorld;

            // ---- the artist's candle
            GameObject _visual = SceneObject("Visual", "VoteCandle");
            foreach (string _part in new[] { "Dish", "Wax", "Wick" })
            {
                Object.DestroyImmediate(_visual.transform.Find(_part).gameObject);
            }
            var _candle = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{PropPrefabsDir}/Candle.prefab"), _visual.transform);
            _candle.name = "ArtistCandle";
            _candle.transform.localScale = Vector3.one * CandleModelScale;
            _candle.transform.SetAsFirstSibling();
            var _vote = new SerializedObject(Object.FindFirstObjectByType<FX.Candle.VoteCandle>(FindObjectsInactive.Include));
            foreach (MeshFilter _filter in _candle.GetComponentsInChildren<MeshFilter>(true))
            {
                if (_filter.name == "Candle")
                {
                    _vote.FindProperty("_wax").objectReferenceValue = _filter;
                }
                else if (_filter.name == "Bit")
                {
                    _vote.FindProperty("_wick").objectReferenceValue = _filter;
                }
            }
            _vote.ApplyModifiedPropertiesWithoutUndo();

            // ---- the fall to the floor
            var _reveal = new SerializedObject(Object.FindFirstObjectByType<Presentation.ChainRevealDirector>(FindObjectsInactive.Include));
            _reveal.FindProperty("_embodiedCamera").objectReferenceValue = Object.FindFirstObjectByType<Avatars.AvatarEmbodiedCamera>(FindObjectsInactive.Include);
            _reveal.ApplyModifiedPropertiesWithoutUndo();

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(_scene);
        }

        // From the old row (world z -7.82, off the table) to just under the cards (-6.4): B1.
        private const float PowerRowWorldShift = 1.42f;
        // The artist's plate at the size of the candle's old dish (radius 1.57 against 0.9075): same place on the table.
        private const float CandleModelScale = 1.73f;

        private static void SetReference(string _label, System.Type _type, string _field, GameObject _value)
        {
            var _so = new SerializedObject(Object.FindFirstObjectByType(_type, FindObjectsInactive.Include));
            _so.FindProperty(_field).objectReferenceValue = _value;
            _so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject SceneObject(string _name, string _parent = null)
        {
            foreach (GameObject _object in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (_object.scene.IsValid() && _object.name == _name && (_parent == null || (_object.transform.parent && _object.transform.parent.name == _parent)))
                {
                    return _object;
                }
            }
            throw new System.InvalidOperationException($"[Props] {_name} not in the scene");
        }

        private static void ConfigureTexture(string _path)
        {
            var _importer = AssetImporter.GetAtPath(_path) as TextureImporter;
            if (!_importer)
            {
                return;
            }
            // A power object is 2 units wide on a 1080p screen: 1024 is plenty. The table and the chairs fill the view.
            bool _large = _path.EndsWith("Prop_Table.png") || _path.EndsWith("Prop_Chair.png");
            int _size = _large ? 2048 : 1024;
            bool _normal = _path.EndsWith("_Normal.png");
            bool _colour = !_normal && !_path.EndsWith("_Gloss.png");
            TextureImporterType _type = _normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (_importer.maxTextureSize == _size && _importer.sRGBTexture == _colour && _importer.mipmapEnabled && _importer.textureType == _type)
            {
                return;
            }
            _importer.textureType = _type;
            _importer.maxTextureSize = _size;
            _importer.sRGBTexture = _colour;
            _importer.mipmapEnabled = true;
            _importer.SaveAndReimport();
        }

        private static Material BuildMaterial(string _name, Look _look)
        {
            string _path = $"{MaterialsDir}/Prop_{_name.Replace(" ", "")}.mat";
            var _material = AssetDatabase.LoadAssetAtPath<Material>(_path);
            if (!_material)
            {
                _material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(_material, _path);
            }
            Texture2D _texture = _look.Texture == null
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesDir}/{_look.Texture}.png");
            if (_look.Texture != null && !_texture)
            {
                Debug.LogError($"[Props] texture missing: {_look.Texture}");
            }

            Texture2D _normalMap = MapOf(_look, "Normal");
            Texture2D _glossMap = MapOf(_look, "Gloss");
            Texture2D _emissionMap = _look.EmissionFromTexture ? _texture : MapOf(_look, "Emission");

            bool _transparent = _look.Alpha < 1f || _look.AlphaFromTexture;
            _material.SetTexture("_BaseMap", _texture);
            Color _colour = _look.Colour;
            _colour.a = _look.Alpha;
            _material.SetColor("_BaseColor", _colour);
            _material.SetFloat("_Metallic", Mathf.Min(_look.Metallic, MetallicCap));
            // With a gloss map the smoothness is its alpha times this.
            _material.SetFloat("_Smoothness", _glossMap ? 1f : 1f - _look.Roughness);
            _material.SetTexture("_MetallicGlossMap", _glossMap);
            SetKeyword(_material, "_METALLICSPECGLOSSMAP", _glossMap);
            _material.SetTexture("_BumpMap", _normalMap);
            _material.SetFloat("_BumpScale", 1f);
            SetKeyword(_material, "_NORMALMAP", _normalMap);

            _material.SetFloat("_Surface", _transparent ? 1f : 0f);
            _material.SetFloat("_Blend", 0f);
            _material.SetFloat("_ZWrite", _transparent ? 0f : 1f);
            _material.SetFloat("_SrcBlend", _transparent ? (float)UnityEngine.Rendering.BlendMode.SrcAlpha : (float)UnityEngine.Rendering.BlendMode.One);
            _material.SetFloat("_DstBlend", _transparent ? (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : (float)UnityEngine.Rendering.BlendMode.Zero);
            _material.SetOverrideTag("RenderType", _transparent ? "Transparent" : "Opaque");
            _material.renderQueue = _transparent ? (int)UnityEngine.Rendering.RenderQueue.Transparent : -1;
            SetKeyword(_material, "_SURFACE_TYPE_TRANSPARENT", _transparent);

            bool _glows = _look.Emission != Color.black;
            SetKeyword(_material, "_EMISSION", _glows);
            _material.globalIlluminationFlags = _glows ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            _material.SetColor("_EmissionColor", _look.Emission);
            _material.SetTexture("_EmissionMap", _emissionMap);
            EditorUtility.SetDirty(_material);
            return _material;
        }

        private static Texture2D MapOf(Look _look, string _kind)
        {
            return _look.Texture == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesDir}/{_look.Texture}_{_kind}.png");
        }

        private static void SetKeyword(Material _material, string _keyword, bool _on)
        {
            if (_on)
            {
                _material.EnableKeyword(_keyword);
            }
            else
            {
                _material.DisableKeyword(_keyword);
            }
        }

        private static void ConfigureModel(string _path, Dictionary<string, Material> _materials)
        {
            var _importer = (ModelImporter)AssetImporter.GetAtPath(_path);
            _importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            _importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            _importer.importAnimation = false;
            _importer.importBlendShapes = false;
            _importer.importCameras = false;
            _importer.importLights = false;
            _importer.animationType = ModelImporterAnimationType.None;
            // The candle melts and the chain is laid on the CPU, the slime of copied powers emits from a model's mesh.
            _importer.isReadable = true;
            _importer.useFileScale = true;
            _importer.globalScale = 1f;
            foreach (Object _asset in AssetDatabase.LoadAllAssetsAtPath(_path))
            {
                if (_asset is Material _slot && !_materials.ContainsKey(_slot.name))
                {
                    Debug.LogError($"[Props] {_path}: no look for the material slot '{_slot.name}'");
                }
            }
            foreach (KeyValuePair<string, Material> _material in _materials)
            {
                _importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), _material.Key), _material.Value);
            }
            _importer.SaveAndReimport();
        }

        private static void BuildPowerPrefab(string _name, PowerObject _power)
        {
            GameObject _root = new GameObject(_name);
            // The FBX's root keeps its own pose (a one-part model carries its half turn there): size and tilt go on a parent.
            GameObject _holder = new GameObject("Holder");
            _holder.transform.SetParent(_root.transform, false);
            GameObject _model = InstantiateModel(_power.Model, _holder.transform);
            Bounds _bounds = BoundsOf(_model);
            float _longest = Mathf.Max(_bounds.size.x, Mathf.Max(_bounds.size.y, _bounds.size.z));
            _holder.transform.localScale = Vector3.one * (_power.Size / _longest);
            _holder.transform.localRotation = Quaternion.Euler(_power.Euler);

            // On the table: the tilted model's lowest point rests at the root's height, and its centre over the root.
            _bounds = BoundsOf(_model);
            _holder.transform.localPosition -= new Vector3(_bounds.center.x, _bounds.min.y, _bounds.center.z);
            _bounds = BoundsOf(_model);

            BoxCollider _collider = _root.AddComponent<BoxCollider>();
            _collider.center = _bounds.center;
            _collider.size = _bounds.size;

            SetLayer(_root, BoardClickableLayer);
            // The power bar is a canvas lying on the table (90 degrees about x) and PowerBarObject3D keeps this rotation:
            // it undoes the canvas's, so the tilt above is the one seen in the world.
            _root.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            string _path = $"{PowerPrefabsDir}/{_name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(_root, _path);
            Object.DestroyImmediate(_root);
        }

        private static void BuildPropPrefab(string _name)
        {
            GameObject _root = new GameObject(_name);
            InstantiateModel(_name, _root.transform);
            PrefabUtility.SaveAsPrefabAsset(_root, $"{PropPrefabsDir}/{_name}.prefab");
            Object.DestroyImmediate(_root);
        }

        private static GameObject InstantiateModel(string _model, Transform _parent)
        {
            var _asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelsDir}/{_model}.fbx");
            return (GameObject)PrefabUtility.InstantiatePrefab(_asset, _parent);
        }

        // In the root's space (the root is at the origin, unrotated, while building).
        private static Bounds BoundsOf(GameObject _model)
        {
            Renderer[] _renderers = _model.GetComponentsInChildren<Renderer>();
            Bounds _bounds = _renderers[0].bounds;
            foreach (Renderer _renderer in _renderers)
            {
                _bounds.Encapsulate(_renderer.bounds);
            }
            return _bounds;
        }

        private static void SetLayer(GameObject _object, int _layer)
        {
            _object.layer = _layer;
            foreach (Transform _child in _object.transform)
            {
                SetLayer(_child.gameObject, _layer);
            }
        }
    }
}
