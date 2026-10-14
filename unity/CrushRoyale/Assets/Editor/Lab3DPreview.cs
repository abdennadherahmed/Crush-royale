using System.IO;
using CrushRoyale.Core.Board;
using CrushRoyale.Game.Gameplay;
using UnityEditor;
using UnityEngine;

namespace CrushRoyale.EditorTools
{
    /// <summary>
    /// Labo 3D : photographie les six gemmes taillees, sans lancer le jeu.
    ///
    /// Un build coute cinquante minutes. Regarder le rendu avant coute dix secondes, et c'est ce qui evite
    /// d'installer un APK pour decouvrir qu'une forme est a l'envers.
    /// </summary>
    public static class Lab3DPreview
    {
        [MenuItem("Crush Royale/Lab 3D/Preview gems")]
        public static void Render()
        {
            string output = Path.GetFullPath("../../lab3d_preview.png");
            Shader shader = Resources.Load<Shader>("Lab3D/Gem3D");
            var material = new Material(shader);

            var root = new GameObject("Lab3DPreview");
            const int layer = 31;
            PieceColor[] colors =
            {
                PieceColor.Red, PieceColor.Blue, PieceColor.Green,
                PieceColor.Yellow, PieceColor.Purple, PieceColor.Orange
            };
            Color[] tints =
            {
                new Color(0.95f, 0.16f, 0.24f), new Color(0.2f, 0.52f, 1f), new Color(0.18f, 0.86f, 0.42f),
                new Color(1f, 0.84f, 0.16f), new Color(0.68f, 0.3f, 0.98f), new Color(1f, 0.52f, 0.12f)
            };
            for (int i = 0; i < colors.Length; i++)
            {
                var go = new GameObject("Gem");
                go.layer = layer;
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = GemMeshes.For(colors[i]);
                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", tints[i]);
                block.SetFloat("_Alpha", 1f);
                block.SetFloat("_Glow", 0f);
                renderer.SetPropertyBlock(block);
                go.transform.position = new Vector3(-250f + i * 100f, 0f, 0f);
                go.transform.rotation = Quaternion.Euler(-22f, 14f, 0f);
                go.transform.localScale = Vector3.one * 42f;
            }

            var camGo = new GameObject("Cam");
            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 60f;
            cam.aspect = 640f / 120f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.1f, 0.07f, 0.19f, 1f);
            cam.cullingMask = 1 << layer;
            cam.transform.position = new Vector3(0f, 0f, -200f);
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 400f;

            var target = new RenderTexture(1280, 240, 16, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = target;
            cam.Render();

            RenderTexture.active = target;
            var image = new Texture2D(1280, 240, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, 1280, 240), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(output, image.EncodeToPNG());
            Debug.Log("Lab3DPreview: " + output);

            Object.DestroyImmediate(root);
            Object.DestroyImmediate(camGo);
            target.Release();
        }
    }
}
