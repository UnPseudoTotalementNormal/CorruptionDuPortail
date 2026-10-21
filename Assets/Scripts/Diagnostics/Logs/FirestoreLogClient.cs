#if CDP_PLAYTEST_LOGS
using System;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Diagnostics.Logs
{
    /// <summary>
    /// Firestore over REST for the playtest logs (no Firebase SDK): anonymous Firebase Auth, the remote switch
    /// (<c>config/logging</c>), the session index document and its log chunks. Security rules let a player create and
    /// update only their own documents and read nothing but the config. Compiled only in playtest builds.
    /// One anonymous account per PC (<c>auth.json</c>, shared by every copy of the game running there), so a copy can
    /// always finish uploading a session another copy started.
    /// </summary>
    internal sealed class FirestoreLogClient
    {
        private const string ProjectId = "corruption-du-portail";
        // Firebase Web API key: identifies the project, not a secret (access is ruled by the security rules).
        private const string ApiKey = "AIzaSyD72okoz-olxXghpFhNaT6XjiWHTVzzVxM";
        private const string DocumentsUrl = "https://firestore.googleapis.com/v1/projects/" + ProjectId + "/databases/(default)/documents";
        private const string StorageUploadUrl = "https://firebasestorage.googleapis.com/v0/b/" + ProjectId + ".firebasestorage.app/o";
        private const int TimeoutSeconds = 20;
        private const double SignUpLockSeconds = 60;

        public enum Result
        {
            Ok,
            /// <summary>The document already exists: a previous try of the same chunk got through.</summary>
            AlreadyExists,
            /// <summary>Refused for good (rules, size, bad request): retrying the same thing will never work.</summary>
            Rejected,
            /// <summary>Network, server or auth trouble: try again later.</summary>
            Failed,
        }

        public enum ConfigStatus { Ok, Missing, Unreachable }

        [Serializable]
        private sealed class SignUpResponse
        {
            public string idToken;
            public string refreshToken;
            public string expiresIn;
            public string localId;
        }

        [Serializable]
        private sealed class RefreshResponse
        {
            public string id_token;
            public string refresh_token;
            public string expires_in;
            public string user_id;
        }

        [Serializable]
        private sealed class StoredAuth
        {
            public string refreshToken;
            public string uid;
        }

        [Serializable] private sealed class StringValue { public string stringValue; }
        [Serializable] private sealed class ConfigFields { public StringValue json; }
        [Serializable] private sealed class ConfigDocument { public ConfigFields fields; }

        private readonly string _authFile;
        private readonly string _lockFile;
        private string _idToken;
        private string _refreshToken;
        private float _tokenExpiresAt;

        public string Uid { get; private set; }

        public FirestoreLogClient(string _directory)
        {
            _authFile = Path.Combine(_directory, "auth.json");
            _lockFile = Path.Combine(_directory, "auth.lock");
            LoadStoredAuth();
        }

        public async UniTask<bool> EnsureAuth(CancellationToken _ct)
        {
            if (_idToken != null && Time.realtimeSinceStartup < _tokenExpiresAt)
            {
                return true;
            }
            if (string.IsNullOrEmpty(_refreshToken))
            {
                LoadStoredAuth(); // another copy of the game may have signed up meanwhile
            }
            if (!string.IsNullOrEmpty(_refreshToken))
            {
                string _body = "grant_type=refresh_token&refresh_token=" + Uri.EscapeDataString(_refreshToken);
                (long _code, string _text) = await Send("POST", "https://securetoken.googleapis.com/v1/token?key=" + ApiKey,
                    _body, "application/x-www-form-urlencoded", false, _ct);
                if (_code == 200)
                {
                    var _refresh = JsonUtility.FromJson<RefreshResponse>(_text);
                    if (!string.IsNullOrEmpty(_refresh?.id_token))
                    {
                        StoreToken(_refresh.id_token, _refresh.refresh_token, _refresh.expires_in, _refresh.user_id);
                        return true;
                    }
                }
                // Only a dead account justifies a new one: a new uid could no longer update the sessions of the old
                // one. Offline, timeouts and server errors just wait for the next try.
                bool _accountGone = _code == 400 && _text != null
                    && (_text.Contains("INVALID_REFRESH_TOKEN") || _text.Contains("USER_NOT_FOUND")
                        || _text.Contains("TOKEN_EXPIRED") || _text.Contains("USER_DISABLED"));
                if (!_accountGone)
                {
                    return false;
                }
                _refreshToken = null;
            }
            return await SignUp(_ct);
        }

        // Only one copy of the game signs up (lock file); the others pick its account up from auth.json.
        private async UniTask<bool> SignUp(CancellationToken _ct)
        {
            FileStream _lock = null;
            try
            {
                try
                {
                    if (File.Exists(_lockFile) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(_lockFile)).TotalSeconds > SignUpLockSeconds)
                    {
                        File.Delete(_lockFile); // left by a copy that died while signing up
                    }
                    _lock = new FileStream(_lockFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
                }
                catch (IOException)
                {
                    return false; // another copy is signing up: its account is read on the next try
                }
                LoadStoredAuth();
                if (!string.IsNullOrEmpty(_refreshToken))
                {
                    return false; // signed up by another copy just now: refresh on the next try
                }
                (long _code, string _text) = await Send("POST", "https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=" + ApiKey,
                    "{\"returnSecureToken\":true}", "application/json", false, _ct);
                if (_code != 200) return false;
                var _signUp = JsonUtility.FromJson<SignUpResponse>(_text);
                if (string.IsNullOrEmpty(_signUp?.idToken)) return false;
                StoreToken(_signUp.idToken, _signUp.refreshToken, _signUp.expiresIn, _signUp.localId);
                return true;
            }
            finally
            {
                _lock?.Dispose();
            }
        }

        /// <summary>The remote switch's JSON. Missing document = switch off; unreachable = unknown.</summary>
        public async UniTask<(ConfigStatus status, string json)> ReadConfig(CancellationToken _ct)
        {
            (long _code, string _text) = await Send("GET", DocumentsUrl + "/config/logging", null, null, true, _ct);
            if (_code == 404) return (ConfigStatus.Missing, null);
            if (_code != 200) return (ConfigStatus.Unreachable, null);
            try
            {
                return (ConfigStatus.Ok, JsonUtility.FromJson<ConfigDocument>(_text)?.fields?.json?.stringValue);
            }
            catch (Exception)
            {
                return (ConfigStatus.Ok, null); // unreadable content: Parse gives null, treated as off
            }
        }

        /// <summary>Creates or updates the session index document (only the given fields).</summary>
        public async UniTask<Result> UpsertSession(string _sessionId, FirestoreFields _fields, CancellationToken _ct)
        {
            string _url = DocumentsUrl + "/sessions/" + Uri.EscapeDataString(_sessionId) + _fields.UpdateMaskQuery();
            (long _code, string _text) = await Send("PATCH", _url, _fields.ToJson(), "application/json", true, _ct);
            return Classify(_code, _text);
        }

        /// <summary>Creates one chunk. <see cref="Result.AlreadyExists"/> = this exact chunk was sent by an earlier try.</summary>
        public async UniTask<Result> CreateChunk(string _sessionId, int _seq, FirestoreFields _fields, CancellationToken _ct)
        {
            string _url = DocumentsUrl + "/sessions/" + Uri.EscapeDataString(_sessionId) + "/chunks?documentId=" + _seq.ToString("D6");
            (long _code, string _text) = await Send("POST", _url, _fields.ToJson(), "application/json", true, _ct);
            return Classify(_code, _text);
        }

        /// <summary>Uploads a report's capture to Storage <c>reports/&lt;id&gt;.jpg</c>.</summary>
        public async UniTask<Result> UploadCapture(string _reportId, byte[] _jpg, CancellationToken _ct)
        {
            string _url = StorageUploadUrl + "?uploadType=media&name=" + Uri.EscapeDataString("reports/" + _reportId + ".jpg");
            (long _code, string _text) = await SendBytes("POST", _url, _jpg, "image/jpeg", _ct);
            return Classify(_code, _text);
        }

        /// <summary>Creates the report document (create-only: a retry of a report that went up answers AlreadyExists).</summary>
        public async UniTask<Result> CreateReport(string _reportId, FirestoreFields _fields, CancellationToken _ct)
        {
            string _url = DocumentsUrl + "/reports?documentId=" + Uri.EscapeDataString(_reportId);
            (long _code, string _text) = await Send("POST", _url, _fields.ToJson(), "application/json", true, _ct);
            return Classify(_code, _text);
        }

        private static Result Classify(long _code, string _text)
        {
            if (_code >= 200 && _code < 300) return Result.Ok;
            if (_code == 409 || (_text != null && _text.Contains("ALREADY_EXISTS"))) return Result.AlreadyExists;
            if (_code == 400 || _code == 403 || _code == 413) return Result.Rejected;
            return Result.Failed; // 0 (network), 401 (token), 429, 5xx
        }

        private void LoadStoredAuth()
        {
            try
            {
                if (!File.Exists(_authFile)) return;
                var _stored = JsonUtility.FromJson<StoredAuth>(File.ReadAllText(_authFile));
                if (string.IsNullOrEmpty(_stored?.refreshToken)) return;
                _refreshToken = _stored.refreshToken;
                Uid = _stored.uid;
            }
            catch (Exception)
            {
                // Being rewritten by another copy: read again next time.
            }
        }

        private void StoreToken(string _newIdToken, string _refresh, string _expiresIn, string _uid)
        {
            _idToken = _newIdToken;
            _refreshToken = _refresh;
            Uid = _uid;
            int.TryParse(_expiresIn, out int _seconds);
            _tokenExpiresAt = Time.realtimeSinceStartup + Mathf.Max(60, (_seconds > 0 ? _seconds : 3600) - 300);
            try
            {
                string _tmp = _authFile + ".tmp";
                File.WriteAllText(_tmp, JsonUtility.ToJson(new StoredAuth { refreshToken = _refresh, uid = _uid }));
                if (File.Exists(_authFile)) File.Replace(_tmp, _authFile, null);
                else File.Move(_tmp, _authFile);
            }
            catch (Exception)
            {
                // Kept in memory for this run.
            }
        }

        private UniTask<(long code, string text)> Send(string _method, string _url, string _body, string _contentType,
            bool _authed, CancellationToken _ct) =>
            Request(_method, _url, _body != null ? Encoding.UTF8.GetBytes(_body) : null, _contentType, _authed, _ct);

        private UniTask<(long code, string text)> SendBytes(string _method, string _url, byte[] _body, string _contentType,
            CancellationToken _ct) =>
            Request(_method, _url, _body, _contentType, true, _ct);

        private async UniTask<(long code, string text)> Request(string _method, string _url, byte[] _body, string _contentType,
            bool _authed, CancellationToken _ct)
        {
            using var _request = new UnityWebRequest(_url, _method);
            if (_body != null)
            {
                _request.uploadHandler = new UploadHandlerRaw(_body);
                _request.SetRequestHeader("Content-Type", _contentType);
            }
            _request.downloadHandler = new DownloadHandlerBuffer();
            _request.timeout = TimeoutSeconds;
            if (_authed && _idToken != null)
            {
                _request.SetRequestHeader("Authorization", "Bearer " + _idToken);
            }
            try
            {
                await _request.SendWebRequest().WithCancellation(_ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // UnityWebRequestException on HTTP errors: the code below tells.
            }
            if (_request.responseCode == 401)
            {
                _idToken = null; // expired or revoked token: refreshed on the next EnsureAuth. A 403 is the rules, not the token.
            }
            long _code = _request.result == UnityWebRequest.Result.ConnectionError ? 0 : _request.responseCode;
            return (_code, _request.downloadHandler?.text);
        }
    }
}
#endif
