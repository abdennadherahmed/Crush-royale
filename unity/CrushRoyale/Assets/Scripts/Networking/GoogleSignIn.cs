using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace CrushRoyale.Game.Networking
{
    /// <summary>
    /// Google sign-in through Android Credential Manager (Assets/Plugins/Android/GoogleSignInBridge.java).
    /// A random nonce is generated here; Google receives its SHA-256, Supabase receives the raw value.
    /// The Google Play Games requirement of the GDD is covered by this Google account sign-in (see docs).
    /// </summary>
    public sealed class GoogleSignIn : MonoBehaviour
    {
        public const string CallbackObjectName = "CrushRoyaleGoogleSignIn";

        private TaskCompletionSource<Result> _pending;
        private string _rawNonce;

        public struct Result
        {
            public bool Success;
            public string IdToken;
            public string RawNonce;
            public string Error;
        }

        public static GoogleSignIn Create(Transform parent)
        {
            var go = new GameObject(CallbackObjectName);
            go.transform.SetParent(parent, false);
            return go.AddComponent<GoogleSignIn>();
        }

        public Task<Result> RequestIdTokenAsync(string webClientId)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (string.IsNullOrEmpty(webClientId) || webClientId.Contains("REPLACE_ME"))
            {
                return Task.FromResult(new Result { Error = "Google web client id is not configured." });
            }
            _pending?.TrySetResult(new Result { Error = "Superseded by a new sign-in request." });
            _pending = new TaskCompletionSource<Result>();
            _rawNonce = NewNonce();

            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var bridge = new AndroidJavaClass("com.crushroyale.signin.GoogleSignInBridge"))
                {
                    bridge.CallStatic("signIn", activity, webClientId, Sha256(_rawNonce), CallbackObjectName);
                }
            }
            catch (AndroidJavaException ex)
            {
                _pending.TrySetResult(new Result { Error = ex.Message });
            }
            return _pending.Task;
#else
            return Task.FromResult(new Result { Error = "Google sign-in is only available on Android devices." });
#endif
        }

        // Called from Java via UnitySendMessage.
        public void OnIdToken(string idToken) =>
            _pending?.TrySetResult(new Result { Success = true, IdToken = idToken, RawNonce = _rawNonce });

        // Called from Java via UnitySendMessage.
        public void OnError(string message) =>
            _pending?.TrySetResult(new Result { Error = message });

        private static string NewNonce()
        {
            byte[] bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Hex(bytes);
        }

        private static string Sha256(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
            }
        }

        private static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }
    }
}
