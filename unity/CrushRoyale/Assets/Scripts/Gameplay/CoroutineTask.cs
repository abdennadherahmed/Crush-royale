using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace CrushRoyale.Game.Gameplay
{
    /// <summary>Awaitable wrapper around coroutines (animations) for async UI flows.</summary>
    public static class CoroutineTask
    {
        public static Task Run(MonoBehaviour host, IEnumerator routine)
        {
            var tcs = new TaskCompletionSource<bool>();
            if (host == null || !host.isActiveAndEnabled)
            {
                tcs.SetResult(false);
                return tcs.Task;
            }
            host.StartCoroutine(Wrap(routine, tcs));
            return tcs.Task;
        }

        public static IEnumerator Tween(float seconds, Action<float> step)
        {
            if (seconds <= 0f)
            {
                step(1f);
                yield break;
            }
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                step(Mathf.Clamp01(t / seconds));
                yield return null;
            }
            step(1f);
        }

        private static IEnumerator Wrap(IEnumerator routine, TaskCompletionSource<bool> tcs)
        {
            yield return routine;
            tcs.TrySetResult(true);
        }
    }

    public static class Ease
    {
        public static float OutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

        public static float InQuad(float t) => t * t;

        public static float OutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }
    }
}
