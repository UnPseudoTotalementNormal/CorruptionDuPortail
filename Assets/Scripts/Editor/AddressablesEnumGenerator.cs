#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CustomAttributes;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

#endregion

public class AddressablesEnumGenerator
{
    [MenuItem("Tools/Generate Addressable Enums")]
    public static void InitGenerateEnums()
    {
        // Initialisation d'Addressables
        var _initOperation = Addressables.InitializeAsync();
        _initOperation.Completed += _operation =>
        {
            if (_operation.Status == AsyncOperationStatus.Succeeded)
            {
                GenerateEnums();
            }
        };
    }

    private static void GenerateEnums()
    {
        var _typesWithAttribute = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(_assembly => _assembly.GetTypes())
            .Where(_type => _type.GetCustomAttribute<AddressableEnumsAttribute>() != null);

        foreach (var _field in _typesWithAttribute)
        {
            var _attribute = _field.GetCustomAttribute<AddressableEnumsAttribute>();
            var _enumFilePath = FindEnumScriptFile(_field);
            if (_enumFilePath == null)
            {
                Debug.LogError($"Enum file not found for {_field.Name}");
                continue;
            }
            
            Debug.Log($"Enum: {_field.Name}, AssetPath: {_attribute.assetPath}, EnumFilePath: {_enumFilePath}");
            
            // Trouver tous les fichiers dans le chemin de l'asset
            var _files = AssetDatabase.FindAssets("*", new[] { _attribute.assetPath })
                .Select(AssetDatabase.GUIDToAssetPath)
                .ToArray();
            
            foreach (var _file in _files)
            {
                var _assetName = _file.Split('/').Last().Split('.').First();
                
                UpdateEnumValue(_enumFilePath, _assetName);
            }
            
            foreach (var _file in _files)
            {
                var _addressableKey = GetAddressableKey(_file);
                if (_addressableKey == "NullAsset")
                {
                    Debug.LogError($"No addressable key found for {_file}");
                    continue;
                }
                var _assetName = _file.Split('/').Last().Split('.').First();
                
                UpdateEnumDictionary(_enumFilePath, _field.Name, _assetName, _addressableKey);
            }
        }
    }
    
    private static void UpdateEnumValue(string _enumFilePath, string _assetName)
    {
        var _lines = System.IO.File.ReadAllLines(_enumFilePath).ToList();

        var _enumStartIndex = _lines.FindIndex(_line => _line.Trim().StartsWith("public enum"));

        if (_lines.Any(_line => _line.Trim().Contains($"{_assetName} =")))
        {
            //Debug.LogWarning($"L'élément {_assetName} existe déjà dans l'énumération.");
            return;
        }

        var _enumClosingBraceIndex = _lines.FindIndex(_enumStartIndex, _line => _line.Trim() == "}");

        // Ajouter le nouvel élément avant l'accolade fermante
        var _existingValues = _lines
            .Where(_line => _line.Trim().EndsWith(","))
            .Select(_line => _line.Trim().TrimEnd(','))
            .Where(_line => int.TryParse(_line.Split('=').LastOrDefault()?.Trim(), out _))
            .Select(_line => int.Parse(_line.Split('=').LastOrDefault()?.Trim() ?? string.Empty))
            .ToHashSet();
        
        var _random = new System.Random();
        int _newValue;
        do
        {
            _newValue = _random.Next(1, int.MaxValue);
        } while (_existingValues.Contains(_newValue));
        
        _lines.Insert(_enumClosingBraceIndex, $"    {_assetName} = {_newValue},");

        // Écrire les modifications dans le fichier
        System.IO.File.WriteAllLines(_enumFilePath, _lines);
        Debug.Log($"Ajouté {_assetName} à l'énumération dans {_enumFilePath}");
    }
    
    private static void UpdateEnumDictionary(string _enumFilePath, string _enumName, string _assetName, string _addressableKey)
    {
        var _lines = System.IO.File.ReadAllLines(_enumFilePath).ToList();
        
        // Vérifier si l'élément existe déjà
        if (_lines.Any(_line => _line.Contains($"{{ {_enumName}.{_assetName}, \"{_addressableKey}\" }}")))
        {
            return;
        }

        var _classStartIndex = _lines.FindIndex(_line => _line.Trim().StartsWith($"public static class {_enumName}Values"));

        // Ajouter la classe après l'énumération si elle n'existe pas
        if (_classStartIndex == -1)
        {
            
            var _enumStartIndex = _lines.FindIndex(_line => _line.Trim().StartsWith("[AddressableEnums"));
            var _enumClosingBraceIndex = _lines.FindIndex(_enumStartIndex, _line => _line.Trim() == "}");
            var _enumLines = _lines.GetRange(_enumStartIndex, _enumClosingBraceIndex - _enumStartIndex + 1);
            
            var _formattedEnumLines = string.Join(Environment.NewLine, _enumLines.Select(_line => $"    {_line}"));
            
            _lines.RemoveRange(_enumStartIndex, _enumClosingBraceIndex - _enumStartIndex + 1);
            
            _lines.Insert(_enumStartIndex, $@"
public static class {_enumName}Values
{{
    public static Dictionary<{_enumName}, string> values = new Dictionary<{_enumName}, string>
    {{
        {{ {_enumName}.{_assetName}, ""{_addressableKey}"" }},
    }};

{_formattedEnumLines}
}}
");
            
            System.IO.File.WriteAllLines(_enumFilePath, _lines);
            return;
        }

        int _dictionaryInitIndex = _lines.FindIndex(_line => _line.Trim().StartsWith($"public static Dictionary<{_enumName}, string> Values"));
        Debug.Assert(_dictionaryInitIndex != -1, $"Impossible de trouver l'initialisation du dictionnaire pour {_enumName}Values dans {_enumFilePath}");

        int _braceIndex = _dictionaryInitIndex + 1;

        int _dictionaryClosingBraceIndex = _lines.FindIndex(_braceIndex, _line => _line.Trim() == "};");
        Debug.Assert(_dictionaryClosingBraceIndex != -1, $"Impossible de trouver l'accolade fermante pour le dictionnaire dans {_enumFilePath}");

        // Ajouter l'entrée au dictionnaire
        _lines.Insert(_dictionaryClosingBraceIndex - 1, $"        {{ {_enumName}.{_assetName}, \"{_addressableKey}\" }},");
        System.IO.File.WriteAllLines(_enumFilePath, _lines);
    }
    
    private static string FindEnumScriptFile(Type _enumType)
    {
        return AssetDatabase.FindAssets($"{_enumType.Name} t:Script")
            .Select(AssetDatabase.GUIDToAssetPath)
            .FirstOrDefault();
    }
    
    private static string GetAddressableKey(string _path)
    {
        foreach (var _locator in Addressables.ResourceLocators)
        {
            foreach (var _key in _locator.Keys)
            {
                if (!_locator.Locate(_key, typeof(object), out IList<IResourceLocation> _locations))
                {
                    continue;
                }
        
                foreach (var _location in _locations)
                {
                    if (_location.InternalId != _path)
                    {
                        continue;
                    }
        
                    return _key.ToString();
                }
            }
        }

        return "NullAsset";
    }
}
