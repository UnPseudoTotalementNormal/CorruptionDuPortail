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
    /// </summary>
    internal sealed class FirestoreLogClient
    {
        private const string ProjectId = "corruption-du-portail";
        // Firebase Web API key: identifies the project, not a secret (access is ruled by the security rules).
        private const string ApiKey = "AIzaSyD72okoz-olxXghpFhNaT6XjiWHTVzzVxM";
        private const string DocumentsUrl = "https://firestore.googleapis.com/v1/projects/" + ProjectId + "/databases/(default)/documents";
        private const int TimeoutSeconds = 20;

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
        private string _idToken;
        private string _refreshToken;
        private float _tokenExpiresAt;

        public string Uid { get; private set; }

        public FirestoreLogClient(string _directory)
        {
            _authFile = Path.Combine(_directory, "auth.json");
            try
            {
                if (File.Exists(_authFile))
                {
                    var _stored = JsonUtility.FromJson<StoredAuth>(File.ReadAllText(_authFile));
                    _refreshToken = _stored?.refreshToken;
                    Uid = _stored?.uid;
                }
            }
            catch (Exception)
            {
                _refreshToken = null;
            }
        }

        public async UniTask<bool> EnsureAuth(CancellationToken _ct)
        {
            if (_idToken != null && Time.realtimeSinceStartup < _tokenExpiresAt)
            {
                return true;
            }
            if (!string.IsNullOrEmpty(_refreshToken))
            {
                string _body = "grant_type=refresh_token&refresh_token=" + Uri.EscapeDataString(_refreshToken);
                (bool _ok, string _text) = await Send("POST", "https://securetoken.googleapis.com/v1/token?key=" + ApiKey,
                    _body, "application/x-www-form-urlencoded", false, _ct);
                if (_ok)
                {
                    var _refresh = JsonUtility.FromJson<RefreshResponse>(_text);
                    if (!string.IsNullOrEmpty(_refresh?.id_token))
                    {
                        StoreToken(_refresh.id_token, _refresh.refresh_token, _refresh.expires_in, _refresh.user_id);
                        return true;
                    }
                }
            }
            (bool _signedUp, string _signUpText) = await Send("POST", "https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=" + ApiKey,
                "{\"returnSecureToken\":true}", "application/json", false, _ct);
            if (!_signedUp) return false;
            var _signUp = JsonUtility.FromJson<SignUpResponse>(_signUpText);
            if (string.IsNullOrEmpty(_signUp?.idToken)) return false;
            StoreToken(_signUp.idToken, _signUp.refreshToken, _signUp.expiresIn, _signUp.localId);
            return true;
        }

        /// <summary>The remote switch's JSON, or null when it can't be read (logs then stay local).</summary>
        public async UniTask<string> ReadConfig(CancellationToken _ct)
        {
            (bool _ok, string _text) = await Send("GET", DocumentsUrl + "/config/logging", null, null, true, _ct);
            if (!_ok) return null;
            try
            {
                return JsonUtility.FromJson<ConfigDocument>(_text)?.fields?.json?.stringValue;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Creates or updates the session index document (only the given fields).</summary>
        public async UniTask<bool> UpsertSession(string _sessionId, FirestoreFields _fields, CancellationToken _ct)
        {
            string _url = DocumentsUrl + "/sessions/" + Uri.EscapeDataString(_sessionId) + _fields.UpdateMaskQuery();
            (bool _ok, _) = await Send("PATCH", _url, _fields.ToJson(), "application/json", true, _ct);
            return _ok;
        }

        /// <summary>Creates one chunk. An already existing chunk (the answer of a previous try was lost) counts as sent.</summary>
        public async UniTask<bool> CreateChunk(string _sessionId, int _seq, FirestoreFields _fields, CancellationToken _ct)
        {
            string _url = DocumentsUrl + "/sessions/" + Uri.EscapeDataString(_sessionId) + "/chunks?documentId=" + _seq.ToString("D6");
            (bool _ok, string _text) = await Send("POST", _url, _fields.ToJson(), "application/json", true, _ct);
            return _ok || (_text != null && _text.Contains("ALREADY_EXISTS"));
        }

        private void StoreToken(string _idToken, string _refresh, string _expiresIn, string _uid)
        {
            this._idToken = _idToken;
            _refreshToken = _refresh;
            Uid = _uid;
            int.TryParse(_expiresIn, out int _seconds);
            _tokenExpiresAt = Time.realtimeSinceStartup + Mathf.Max(60, (_seconds > 0 ? _seconds : 3600) - 300);
            try
            {
                File.WriteAllText(_authFile, JsonUtility.ToJson(new StoredAuth { refreshToken = _refresh, uid = _uid }));
            }
            catch (Exception)
            {
                // Next launch signs up again: a new anonymous id, nothing lost but the crash-tail of this session.
            }
        }

        private async UniTask<(bool ok, string text)> Send(string _method, string _url, string _body, string _contentType,
            bool _authed, CancellationToken _ct)
        {
            using var _request = new UnityWebRequest(_url, _method);
            if (_body != null)
            {
                _request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(_body));
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
                // UnityWebRequestException on HTTP errors: the result below tells.
            }
            string _text = _request.downloadHandler?.text;
            if (_request.responseCode == 401 || _request.responseCode == 403)
            {
                _idToken = null; // forces a refresh next time
            }
            return (_request.result == UnityWebRequest.Result.Success, _text);
        }
    }
}
#endif
