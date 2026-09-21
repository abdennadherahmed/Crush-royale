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
    /// Renders every screen of the game to a PNG without opening the project, so a broken screen is visible in CI.
    /// Runs in the editor (batchmode WITHOUT -nographics, a graphics device is required): the UI is built from code,
    /// so the screens only need the service hub plus a canvas pointed at a render texture.
    /// </summary>
    public static class ScreenshotTool
    {
        private const int Width = 1080;
        private const int Height = 1920;

        [MenuItem("Crush Royale/5. Capture Screenshots", priority = 21)]
        public static void CaptureAll()
        {
            string output = Path.GetFullPath(GetArgument("-screenshotOutput") ?? "screenshots");
            Directory.CreateDirectory(output);

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                // -nographics gives a null device: nothing can be rendered, and every PNG would be empty.
                Debug.LogError("ScreenshotTool: no graphics device. Unity must run WITHOUT -nographics.");
                EditorApplication.Exit(1);
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameRoot game = OfflineHarness.BootGame();
            Camera camera = CreateCamera();
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            camera.targetTexture = target;
            PrepareCanvas(game.UI.Canvas, camera);

            var written = new List<string>();
            int failed = 0;
            int blank = 0;
            foreach (ScreenCase test in OfflineHarness.Screens())
            {
                Exception error;
                bool uniform = false;
                try
                {
                    error = Capture(game, camera, target, test, output, out uniform);
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

            camera.targetTexture = null;
            RenderTexture.active = null;
            target.Release();
            WriteIndex(output, written);
            Debug.Log("ScreenshotTool: " + written.Count + " screenshot(s) in " + output + ", " + failed + " screen(s) failed, " + blank + " blank.");

            // A screen that throws is exactly what this job is for, so it must fail the build; so must a dead renderer.
            if (failed > 0 || written.Count == 0 || blank == written.Count)
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
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10f;
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = 1f;
            }
        }

        /// <summary>
        /// Builds one screen, renders it and always writes the PNG, even when the build threw: a picture of the
        /// broken screen is the whole point of this job. Returns the build error, or null.
        /// </summary>
        private static Exception Capture(GameRoot game, Camera camera, RenderTexture target, ScreenCase test, string output, out bool uniform)
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
                camera.Render();
                Canvas.ForceUpdateCanvases();
                camera.Render();
                uniform = WritePng(target, Path.Combine(output, test.Name + ".png"));
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
