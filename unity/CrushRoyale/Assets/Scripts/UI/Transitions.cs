using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.UI
{
    /// <summary>
    /// Page and dialog motion. Navigation has a direction: going deeper pushes the new page in from the right and
    /// zooms it up from slightly small, coming back pulls it in from the left and settles down from slightly large,
    /// and back is noticeably quicker because the player already knows where they are going. A cross-fade alone read
    /// as "the screen was replaced"; the direction is what makes it read as "I moved".
    /// Everything here is a plain coroutine over unscaled time and touches only a CanvasGroup and a transform, so it
    /// costs nothing but the frames it animates, and it degrades to a short fade under reduced motion.
    /// </summary>
    public static class Transitions
    {
        public const float ForwardDuration = 0.26f;
        public const float BackDuration = 0.17f;

        /// <summary>How far a page travels, in canvas units. Short: a long slide feels sluggish on a phone.</summary>
        private const float Travel = 130f;

        public static bool ReduceMotion => GameRoot.Instance != null && GameRoot.Instance.Save.Settings.ReduceMotion;

        /// <summary>Ease-out-cubic: almost all the distance is covered in the first half, so the page feels snappy.</summary>
        public static float Ease(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);

        /// <summary>Slides and fades <paramref name="incoming"/> in while <paramref name="outgoing"/> leaves.</summary>
        public static IEnumerator Page(CanvasGroup incoming, CanvasGroup outgoing, bool back)
        {
            bool plain = ReduceMotion;
            float duration = back || plain ? BackDuration : ForwardDuration;
            var inRect = (RectTransform)incoming.transform;
            RectTransform outRect = outgoing != null ? (RectTransform)outgoing.transform : null;
            float from = back ? -Travel : Travel;
            float zoom = back ? 1.04f : 0.955f;

            for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
            {
                float k = Ease(t / duration);
                incoming.alpha = k;
                if (!plain)
                {
                    inRect.anchoredPosition = new Vector2(Mathf.LerpUnclamped(from, 0f, k), 0f);
                    float scale = Mathf.LerpUnclamped(zoom, 1f, k);
                    inRect.localScale = new Vector3(scale, scale, 1f);
                    if (outRect != null)
                    {
                        // The old page leaves the other way and sinks slightly: it goes behind, not just away.
                        outRect.anchoredPosition = new Vector2(Mathf.LerpUnclamped(0f, -from * 0.55f, k), 0f);
                        float outScale = Mathf.LerpUnclamped(1f, back ? 0.96f : 1.03f, k);
                        outRect.localScale = new Vector3(outScale, outScale, 1f);
                    }
                }
                if (outgoing != null)
                {
                    // The outgoing page gives up its alpha faster than the new one takes it: no muddy double image.
                    outgoing.alpha = 1f - Mathf.Clamp01(k * 1.4f);
                }
                yield return null;
            }
            incoming.alpha = 1f;
            inRect.anchoredPosition = Vector2.zero;
            inRect.localScale = Vector3.one;
        }

        /// <summary>
        /// Modal entrance: the dim fades up instead of snapping on, and the page underneath is pushed back a hair.
        /// A real blur would cost a render texture and a full-screen pass, which a mid-range phone cannot spare; the
        /// receding scale plus a soft dim gives the same "the page is out of focus behind this" read for free.
        /// </summary>
        public static IEnumerator Dim(CanvasGroup shade, RectTransform behind)
        {
            const float duration = 0.18f;
            Vector3 rest = behind != null ? behind.localScale : Vector3.one;
            for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
            {
                float k = Ease(t / duration);
                shade.alpha = k;
                if (behind != null && !ReduceMotion)
                {
                    float scale = Mathf.LerpUnclamped(1f, 0.975f, k);
                    behind.localScale = new Vector3(rest.x * scale, rest.y * scale, 1f);
                }
                yield return null;
            }
            shade.alpha = 1f;
        }

        /// <summary>Puts the page behind a closing modal back where it was.</summary>
        public static void Restore(RectTransform behind)
        {
            if (behind != null)
            {
                behind.localScale = Vector3.one;
            }
        }

        /// <summary>Vignette over the whole screen: darker at the edges so a modal sits in a pool of light.</summary>
        public static Image Vignette(Transform parent, Color color)
        {
            Image image = UIFactory.Panel("Vignette", parent, color, rounded: false);
            UIFactory.Stretch(image.rectTransform);
            image.sprite = ProceduralSprites.Vignette();
            image.type = Image.Type.Simple;
            image.raycastTarget = false;
            return image;
        }
    }

    /// <summary>
    /// Backdrop of a modal: a dim that fades up with a vignette, while the page behind recedes a little. Destroying
    /// it (which is how dialogs and popups already close) puts the page back, so callers need no teardown code.
    /// </summary>
    public sealed class ModalShade : MonoBehaviour
    {
        private RectTransform _behind;

        public static Image Create(Transform layer, RectTransform behind, Color color)
        {
            Image shade = UIFactory.Panel("Shade", layer, color, rounded: false);
            UIFactory.Stretch(shade.rectTransform);
            Transitions.Vignette(shade.transform, new Color(0f, 0f, 0f, 0.55f));
            CanvasGroup group = shade.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            ModalShade component = shade.gameObject.AddComponent<ModalShade>();
            component._behind = behind;
            component.StartCoroutine(Transitions.Dim(group, behind));
            return shade;
        }

        private void OnDestroy() => Transitions.Restore(_behind);
    }
}
