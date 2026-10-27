using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Network;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Player builds of both online flavors (<see cref="GameFlavor"/>): Relay (UGS + Unity Relay, several copies on
/// one PC) and Steam (Steam lobbies + Steam Datagram Relay). The Steam build only adds the CDP_STEAM define, for
/// this build alone: ProjectSettings keep no flavor, so the Editor and the autoplay builds stay Relay.
/// The command line must say <c>-cdpLogs on</c> (playtest build: CDP_PLAYTEST_LOGS, online logs,
/// Diagnostics/Logs/GameLog) or <c>-cdpLogs off</c> (public build: no log code, URL or key compiled in). Anything else
/// fails the build, so a public build can never ship the logs by omission or typo.
/// CI (Build.yml) calls <see cref="BuildFromCommandLine"/> through game-ci's <c>buildMethod</c>.
/// </summary>
public static class CdpBuild
{
    private const string SteamPlayModeMenu = "Tools/Corruption du Portail/Build Steam en Play mode";

    /// <summary>
    /// game-ci entry point. Reads game-ci's <c>-customBuildPath</c>, <c>-buildTarget</c>, <c>-buildVersion</c>
    /// and our <c>-cdpFlavor relay|steam</c> and <c>-cdpLogs on|off</c> (Build.yml <c>customParameters</c>). Exits 1 on failure.
    /// </summary>
    public static void BuildFromCommandLine()
    {
        Dictionary<string, string> _args = ParseArgs(Environment.GetCommandLineArgs());
        bool _steam = _args.TryGetValue("cdpFlavor", out string _flavor) && _flavor.Equals("steam", StringComparison.OrdinalIgnoreCase);
        _args.TryGetValue("cdpLogs", out string _logs);
        bool _playtestLogs = string.Equals(_logs, "on", StringComparison.OrdinalIgnoreCase);
        if (!_playtestLogs && !string.Equals(_logs, "off", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError($"[BUILD] -cdpLogs must be 'on' (playtest) or 'off' (public), got '{_logs ?? "nothing"}'.");
            EditorApplication.Exit(1);
            return;
        }

        if (!_args.TryGetValue("customBuildPath", out string _path) || string.IsNullOrEmpty(_path))
        {
            Debug.LogError("[BUILD] -customBuildPath missing.");
            EditorApplication.Exit(1);
            return;
        }
        // game-ci passes the executable's full path; accept a bare folder too (then named after -customBuildName).
        if (string.IsNullOrEmpty(Path.GetExtension(_path)))
        {
            string _name = _args.TryGetValue("customBuildName", out string _buildName) && !string.IsNullOrEmpty(_buildName)
                ? _buildName
                : PlayerSettings.productName;
            _path = Path.Combine(_path, _name + ".exe");
        }
        BuildTarget _target = _args.TryGetValue("buildTarget", out string _rawTarget)
            && Enum.TryParse(_rawTarget, out BuildTarget _parsed)
            ? _parsed
            : BuildTarget.StandaloneWindows64;
        // game-ci computes the version from git (semantic versioning); the join gate compares it between players.
        if (_args.TryGetValue("buildVersion", out string _version) && !string.IsNullOrEmpty(_version) && _version != "none")
        {
            PlayerSettings.bundleVersion = _version;
        }

        BuildReport _report = Build(_target, _path, _steam, BuildOptions.None, _playtestLogs);
        if (_report.summary.result != BuildResult.Succeeded)
        {
            EditorApplication.Exit(1);
        }
    }

    /// <summary>
    /// Autoplay build (<c>tools/autoplay/unityctl.sh build</c>): Development Relay player with the playtest logs compiled
    /// in, so autoplay games write the same GameLog lines as a playtest: locally (read with <c>logs.py --local</c>),
    /// online only with <c>-playtest-logs-upload</c>, always marked autoplay. Writes <c>build-result.json</c> next to
    /// the exe once done (the wrapper waits for it).
    /// </summary>
    public static void BuildAutoplay()
    {
        string _path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Autoplay", PlayerSettings.productName + ".exe"));
        string _resultPath = Path.Combine(Path.GetDirectoryName(_path), "build-result.json");
        var _result = new AutoplayBuildResult { result = "Exception" };
        try
        {
            if (File.Exists(_resultPath)) File.Delete(_resultPath);
            BuildReport _report = Build(BuildTarget.StandaloneWindows64, _path, false, BuildOptions.Development, true);
            _result.result = _report.summary.result.ToString();
            _result.errors = _report.summary.totalErrors;
        }
        catch (Exception _e)
        {
            _result.message = _e.Message;
            Debug.LogException(_e);
        }
        finally
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_resultPath));
            File.WriteAllText(_resultPath, JsonUtility.ToJson(_result));
        }
    }

    [Serializable]
    private sealed class AutoplayBuildResult
    {
        public string result;
        public int errors;
        public string message;
    }

    public static BuildReport Build(BuildTarget target, string locationPath, bool steam, BuildOptions options, bool playtestLogs = false)
    {
        var _defines = new List<string>();
        if (steam) _defines.Add(GameFlavor.SteamDefine);
        if (playtestLogs) _defines.Add(Diagnostics.Logs.GameLog.Define);

        var _options = new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = locationPath,
            target = target,
            targetGroup = BuildPipeline.GetBuildTargetGroup(target),
            options = options,
            extraScriptingDefines = _defines.ToArray(),
        };

        Debug.Log($"[BUILD] {(steam ? "Steam" : "Relay")} build{(playtestLogs ? " + playtest logs" : "")}, {target}, version {PlayerSettings.bundleVersion} -> {locationPath}");
        BuildReport _report = BuildPipeline.BuildPlayer(_options);
        Debug.Log($"[BUILD] {_report.summary.result}: {_report.summary.totalErrors} error(s), {_report.summary.totalSize / (1024 * 1024)} MB.");

        if (steam && _report.summary.result == BuildResult.Succeeded)
        {
            // Lets the exe start Steam's API when launched outside the Steam library (testers unzip the CI build).
            // Drop it once the game ships on its own app id and is launched by Steam.
            string _folder = Path.GetDirectoryName(Path.GetFullPath(locationPath));
            File.WriteAllText(Path.Combine(_folder, "steam_appid.txt"), GameValues.STEAM_APP_ID.ToString());
        }
        return _report;
    }

    // "-key value" pairs; a key without value maps to "".
    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var _parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("-"))
            {
                continue;
            }
            string _key = args[i].TrimStart('-');
            bool _hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("-");
            _parsed[_key] = _hasValue ? args[++i] : string.Empty;
        }
        return _parsed;
    }

    // ---- Editor: play the Steam flavor (needs the Steam client running on this PC) ----

    [MenuItem(SteamPlayModeMenu)]
    private static void ToggleSteamPlayMode()
    {
        bool _steam = !EditorPrefs.GetBool(GameFlavor.EditorPrefKey, false);
        EditorPrefs.SetBool(GameFlavor.EditorPrefKey, _steam);
        Debug.Log($"[BUILD] Play mode now runs the {(_steam ? "Steam" : "Relay")} flavor.");
    }

    [MenuItem(SteamPlayModeMenu, true)]
    private static bool ToggleSteamPlayModeValidate()
    {
        Menu.SetChecked(SteamPlayModeMenu, EditorPrefs.GetBool(GameFlavor.EditorPrefKey, false));
        return !EditorApplication.isPlaying;
    }
}
