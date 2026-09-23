using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CrushRoyale.Game;
using CrushRoyale.Game.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace CrushRoyale.EditorTools
{
    /// <summary>
    /// Renders every screen of the game to a PNG without opening the project, so a broken screen is visible in CI,
    /// and audits every label against the box it was given. The pictures need a graphics device (batchmode WITHOUT
    /// -nographics); the audit does not, so a runner with no GPU still reports the overflows and the overlaps.
    /// </summary>
    public static class ScreenshotTool
    {
        /// <summary>Below this a caption is unreadable on a phone at arm length.</summary>
        private const int MinReadableFontSize = 18;

        /// <summary>Labels may share a few pixels; a third of the smaller one covered is a bug.</summary>
        private const float OverlapTolerance = 0.33f;

        /// <summary>A label less than half inside its scroll viewport is off-screen and is not audited.</summary>
        private const float VisibleShare = 0.5f;

        private const int Width = 1080;
        private const int Height = 1920;

        [MenuItem("Crush Royale/5. Capture Screenshots", priority = 21)]
        public static void CaptureAll()
        {
            string output = Path.GetFullPath(GetArgument("-screenshotOutput") ?? "screenshots");
            Directory.CreateDirectory(output);

            // A GitHub runner has no GPU. When the device is missing the PNGs are impossible, but the layout audit
            // below (text that overflows its box, texts that sit on top of each other) still works and is the part
            // that actually finds the bugs, so the job degrades instead of failing with nothing to show.
            bool canRender = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            if (!canRender)
            {
                Debug.LogWarning("ScreenshotTool: no graphics device, writing the layout audit only.");
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameRoot game = OfflineHarness.BootGame();
            Camera camera = canRender ? CreateCamera() : null;
            RenderTexture target = null;
            if (canRender)
            {
                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                camera.targetTexture = target;
            }
            PrepareCanvas(game.UI.Canvas, camera);
            var audit = new List<string>();

            var written = new List<string>();
            int failed = 0;
            int blank = 0;
            foreach (ScreenCase test in OfflineHarness.Screens())
            {
                Exception error;
                bool uniform = false;
                try
                {
                    error = Capture(game, camera, target, test, output, audit, out uniform);
                }
                catch (Exception ex)
                {
                    // Rendering or writing itself failed: keep going so the other screens are still captured.
                    error = ex;
                }
                written.Add(test.Name);
                if (error != null)
                {
                    // The half-built screen is still written out: the PNG shows how far it got.
                    failed++;
                    Debug.LogError("ScreenshotTool: " + test.Name + " threw while building.");
                    Debug.LogException(error);
                }
                if (uniform)
                {
                    blank++;
                    Debug.LogWarning("ScreenshotTool: " + test.Name + " rendered a single flat colour.");
                }
            }

            if (canRender)
            {
                camera.targetTexture = null;
                RenderTexture.active = null;
                target.Release();
            }
            WriteLayoutReport(output, audit);
            Debug.Log("ScreenshotTool: layout audit found " + audit.Count + " issue(s).");
            WriteIndex(output, written);
            Debug.Log("ScreenshotTool: " + written.Count + " screenshot(s) in " + output + ", " + failed + " screen(s) failed, " + blank + " blank.");

            // A screen that throws is exactly what this job is for, so it must fail the build; so must a dead renderer.
            if (failed > 0 || written.Count == 0 || (canRender && blank == written.Count))
            {
                EditorApplication.Exit(1);
            }
        }

        private static Camera CreateCamera()
        {
            var go = new GameObject("ScreenshotCamera");
            Camera camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Theme.Background;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            return camera;
        }

        /// <summary>
        /// The game canvas is a screen-space overlay, which no camera can render: pointed at the camera instead, it is
        /// sized by the render texture (1080x1920), which is exactly the reference resolution of the UI.
        /// </summary>
        private static void PrepareCanvas(Canvas canvas, Camera camera)
        {
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = 1f;
            }
            if (camera != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 10f;
                return;
            }

            // No camera: an overlay canvas would be sized by a batchmode "screen" of whatever resolution, and every
            // measurement would be taken on the wrong aspect. A world-space canvas takes the size it is given.
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = null;
            var rect = (RectTransform)canvas.transform;
            rect.sizeDelta = new Vector2(Width, Height);
            rect.localScale = Vector3.one;
            rect.position = Vector3.zero;
        }

        /// <summary>
        /// Builds one screen, renders it and always writes the PNG, even when the build threw: a picture of the
        /// broken screen is the whole point of this job. Returns the build error, or null.
        /// </summary>
        private static Exception Capture(GameRoot game, Camera camera, RenderTexture target, ScreenCase test, string output, List<string> audit, out bool uniform)
        {
            OfflineHarness.SetProfile(game, test.WithProfile);
            Canvas canvas = game.UI.Canvas;
            RectTransform host = UIFactory.Stretch(UIFactory.Rect(test.Name, canvas.transform));
            // Behind the dialog and toast layers, like a real screen.
            host.SetSiblingIndex(0);
            Exception error = null;
            try
            {
                try
                {
                    object args = test.Args != null ? test.Args(game) : null;
                    var screen = (UIScreen)host.gameObject.AddComponent(test.Type);
                    screen.Setup(game.UI, args);

                    // The step where a screen loads what it shows.
                    //
                    // Setup only builds the layout; screens that fetch their contents do it here, so leaving this out
                    // meant every one of them was captured on its "Loading" frame. The shop was photographed empty
                    // in every build ever taken for exactly this reason, and two defects reported in its grid were
                    // invisible to me because of it. Offline the work is synchronous, so the task is already
                    // finished by the time it is returned; anything that genuinely waits on a server is skipped
                    // rather than hanging the capture.
                    System.Threading.Tasks.Task shown = screen.OnShownAsync();
                    if (shown != null && shown.IsFaulted)
                    {
                        Debug.LogWarning("OnShownAsync failed on " + test.Name + ": " + shown.Exception?.GetBaseException().Message);
                    }
                    test.Overlay?.Invoke(game);
                }
                catch (Exception ex)
                {
                    error = ex;
                }

                Settle(canvas.transform);
                // Twice: layout groups and content-size fitters settle on the second pass, and the first render is
                // what makes the dynamic font build the glyphs the second one actually draws.
                Canvas.ForceUpdateCanvases();
                if (camera != null)
                {
                    camera.Render();
                    Canvas.ForceUpdateCanvases();
                    camera.Render();
                }
                AuditLayout(test.Name, host, audit);
                uniform = camera != null && WritePng(target, Path.Combine(output, test.Name + ".png"));
                return error;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host.gameObject);
                ClearLayer(game.UI.DialogLayer);
            }
        }

        /// <summary>
        /// Edit mode never runs Update, so anything that animates itself into place would be frozen at its start value
        /// (PopIn opens at scale zero). The animators are removed and every transform is put back at rest instead.
        /// </summary>
        private static void Settle(Transform root)
        {
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour is PopIn || behaviour is Pulse || behaviour is Breathe || behaviour is Spinner || behaviour is ButtonFeedback)
                {
                    UnityEngine.Object.DestroyImmediate(behaviour);
                }
            }
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                child.localScale = Vector3.one;
            }
            foreach (CanvasGroup group in root.GetComponentsInChildren<CanvasGroup>(true))
            {
                group.alpha = 1f;
            }
        }

        private static void ClearLayer(Transform layer)
        {
            for (int i = layer.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(layer.GetChild(i).gameObject);
            }
        }

        private static bool WritePng(RenderTexture target, string path)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;

            File.WriteAllBytes(path, texture.EncodeToPNG());
            bool uniform = IsUniform(texture);
            UnityEngine.Object.DestroyImmediate(texture);
            return uniform;
        }

        /// <summary>A flat image means the renderer produced nothing: worth failing the job over.</summary>
        private static bool IsUniform(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            Color32 first = pixels[0];
            for (int i = 1; i < pixels.Length; i += 37)
            {
                Color32 pixel = pixels[i];
                if (pixel.r != first.r || pixel.g != first.g || pixel.b != first.b)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// The part of this job that finds bugs without anyone looking: every label is measured against the box it
        /// was given. A chip whose text wraps to two lines, an epithet sitting under a chip, a caption too small
        /// to read on a phone: all of those shipped before, because nobody could see the screens.
        /// </summary>
        private static void AuditLayout(string screen, RectTransform host, List<string> audit)
        {
            Text[] labels = host.GetComponentsInChildren<Text>(true);
            var boxes = new List<KeyValuePair<Text, Rect>>();
            foreach (Text label in labels)
            {
                if (label == null || !label.gameObject.activeInHierarchy || string.IsNullOrEmpty(label.text))
                {
                    continue;
                }
                if (!MostlyVisible(label.rectTransform))
                {
                    // Scrolled out of its list: its box still has world coordinates and used to be reported as
                    // overlapping whatever sits over the viewport, which is how a perfectly fine VIP page came back
                    // with "two labels overlap (100%)".
                    continue;
                }
                RectTransform rect = label.rectTransform;
                float width = rect.rect.width;
                float height = rect.rect.height;
                if (width <= 1f || height <= 1f)
                {
                    audit.Add(screen + " | zero-sized label | " + Shorten(label.text));
                    continue;
                }

                float preferredWidth = 0f;
                float preferredHeight = 0f;
                try
                {
                    preferredWidth = label.preferredWidth;
                    preferredHeight = label.preferredHeight;
                }
                catch (Exception)
                {
                    // Font metrics can be unavailable headless; the geometry checks below still run.
                }

                bool wraps = label.horizontalOverflow == HorizontalWrapMode.Wrap;
                if (!label.resizeTextForBestFit)
                {
                    if (!wraps && preferredWidth > width + 2f)
                    {
                        audit.Add(screen + " | text wider than its box (" + Mathf.RoundToInt(preferredWidth) + " > "
                            + Mathf.RoundToInt(width) + ") | " + Shorten(label.text));
                    }
                    else if (wraps && preferredHeight > height + 2f)
                    {
                        audit.Add(screen + " | wrapped text taller than its box (" + Mathf.RoundToInt(preferredHeight)
                            + " > " + Mathf.RoundToInt(height) + ") | " + Shorten(label.text));
                    }
                }

                // A format string used as a plain caption printed "{0} coins" verbatim under every wallet tile.
                if (label.text.Contains("{0}") || label.text.Contains("{1}") || label.text.Contains("{2}"))
                {
                    audit.Add(screen + " | unformatted placeholder on screen | " + Shorten(label.text));
                }

                // The size actually drawn, not the floor the label is allowed to shrink to.
                //
                // Reading resizeTextMinSize reported every best-fit label in the game: a caption rendering at 28 px
                // was flagged because it was permitted to go down to 14 if it ever had to. That was 140 of the 150
                // lines in this report, which made the whole thing unreadable and hid the three real defects in it.
                int drawn = label.resizeTextForBestFit && label.cachedTextGenerator != null
                    && label.cachedTextGenerator.fontSizeUsedForBestFit > 0
                    ? label.cachedTextGenerator.fontSizeUsedForBestFit
                    : label.fontSize;
                if (drawn > 0 && drawn < MinReadableFontSize)
                {
                    audit.Add(screen + " | font size " + drawn + " below " + MinReadableFontSize + " | " + Shorten(label.text));
                }
                boxes.Add(new KeyValuePair<Text, Rect>(label, WorldRect(rect)));
            }

            CheckCentring(host, screen, audit);

            for (int i = 0; i < boxes.Count; i++)
            {
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    Text a = boxes[i].Key;
                    Text b = boxes[j].Key;
                    if (a.transform.IsChildOf(b.transform) || b.transform.IsChildOf(a.transform))
                    {
                        continue;
                    }
                    float overlap = Intersection(boxes[i].Value, boxes[j].Value);
                    if (overlap <= 0f)
                    {
                        continue;
                    }
                    float smaller = Mathf.Min(Area(boxes[i].Value), Area(boxes[j].Value));
                    if (smaller > 0f && overlap / smaller > OverlapTolerance)
                    {
                        audit.Add(screen + " | two labels overlap (" + Mathf.RoundToInt(overlap / smaller * 100f)
                            + "%) | " + Shorten(a.text) + " + " + Shorten(b.text));
                    }
                }
            }
        }

        /// <summary>False for a label scrolled out of the list it lives in: only what a player can see is audited.</summary>
        private static bool MostlyVisible(RectTransform rect)
        {
            Rect box = WorldRect(rect);
            float area = Area(box);
            if (area <= 0f)
            {
                return true;
            }
            for (Transform parent = rect.parent; parent != null; parent = parent.parent)
            {
                var mask = parent.GetComponent<RectMask2D>();
                if (mask == null || !mask.enabled)
                {
                    continue;
                }
                if (Intersection(box, WorldRect((RectTransform)parent)) / area < VisibleShare)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Finds artwork and text that hangs out of the thing holding it, and does so unevenly.
        ///
        /// Something wider than its container but overhanging equally on both sides is a deliberate bleed. Something
        /// overhanging on one side only is a centring mistake -- and it is the mistake that kept being reported by
        /// eye: a gear whose teeth sat over the right edge of its plate, a map icon pushed sideways inside its own
        /// file, a caption running off a tile. Measuring it costs nothing and stops those reaching the phone.
        /// </summary>
        private static void CheckCentring(RectTransform host, string screen, List<string> audit)
        {
            const float tolerance = 6f;
            foreach (Graphic graphic in host.GetComponentsInChildren<Graphic>(false))
            {
                var rect = graphic.rectTransform;
                var parent = rect.parent as RectTransform;
                if (parent == null || !MostlyVisible(rect) || graphic is Text text && string.IsNullOrWhiteSpace(text.text))
                {
                    continue;
                }
                Rect self = WorldRect(rect);
                Rect box = WorldRect(parent);
                if (self.width <= 1f || box.width <= 1f)
                {
                    continue;
                }
                float left = box.xMin - self.xMin;
                float right = self.xMax - box.xMax;
                // Both sides inside, or both hanging out by the same amount: nothing to report.
                if (left <= tolerance && right <= tolerance)
                {
                    continue;
                }
                if (Mathf.Abs(left - right) <= tolerance)
                {
                    continue;
                }
                string what = graphic is Text t ? Shorten(t.text) : graphic.gameObject.name;
                audit.Add(screen + " | sits off centre in its box (" + Mathf.RoundToInt(left) + " px left, "
                    + Mathf.RoundToInt(right) + " px right) | " + what);
            }
        }

        private static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            float minX = Mathf.Min(corners[0].x, corners[2].x);
            float maxX = Mathf.Max(corners[0].x, corners[2].x);
            float minY = Mathf.Min(corners[0].y, corners[2].y);
            float maxY = Mathf.Max(corners[0].y, corners[2].y);
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private static float Area(Rect rect)
        {
            return Mathf.Max(0f, rect.width) * Mathf.Max(0f, rect.height);
        }

        private static float Intersection(Rect a, Rect b)
        {
            float width = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float height = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return width <= 0f || height <= 0f ? 0f : width * height;
        }

        private static string Shorten(string text)
        {
            string flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return flat.Length <= 42 ? flat : flat.Substring(0, 40) + "...";
        }

        /// <summary>The audit as a file anyone can read.</summary>
        private static void WriteLayoutReport(string output, List<string> audit)
        {
            var report = new StringBuilder();
            report.AppendLine("# Layout audit");
            report.AppendLine();
            if (audit.Count == 0)
            {
                report.AppendLine("No label overflows its box, sits on another label, or is too small to read.");
            }
            else
            {
                report.AppendLine(audit.Count + " issue(s). Columns: screen | problem | text.");
                report.AppendLine();
                audit.Sort(StringComparer.Ordinal);
                foreach (string line in audit)
                {
                    report.AppendLine("- " + line);
                }
            }
            File.WriteAllText(Path.Combine(output, "layout-audit.md"), report.ToString());
        }

        /// <summary>Contact sheet so the artifact can be read by opening one file.</summary>
        private static void WriteIndex(string output, List<string> names)
        {
            var html = new StringBuilder();
            html.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>Crush Royale screens</title>");
            html.AppendLine("<style>body{background:#140F24;color:#F4F1FF;font-family:sans-serif;margin:24px}");
            html.AppendLine("figure{display:inline-block;margin:0 16px 24px 0;text-align:center}img{width:270px;border:1px solid #3A3068}</style>");
            html.AppendLine("</head><body><h1>Crush Royale screens</h1>");
            foreach (string name in names)
            {
                html.AppendLine("<figure><img src=\"" + name + ".png\" alt=\"" + name + "\"><figcaption>" + name + "</figcaption></figure>");
            }
            html.AppendLine("</body></html>");
            File.WriteAllText(Path.Combine(output, "index.html"), html.ToString());
        }

        private static string GetArgument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name && !string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }
}
