using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace CrushRoyale.Game
{
    /// <summary>Marshals callbacks from background threads (realtime socket, HTTP events) onto Unity's main thread.</summary>
    public sealed class MainThread : MonoBehaviour
    {
        private static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();
        private static MainThread _instance;

        public static void EnsureExists(Transform parent)
        {
            if (_instance != null)
            {
                return;
            }
            var go = new GameObject("MainThread");
            go.transform.SetParent(parent, false);
            _instance = go.AddComponent<MainThread>();
        }

        public static void Post(Action action)
        {
            if (action != null)
            {
                Queue.Enqueue(action);
            }
        }

        private void Update()
        {
            int budget = 256;
            while (budget-- > 0 && Queue.TryDequeue(out Action action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }
    }
}
