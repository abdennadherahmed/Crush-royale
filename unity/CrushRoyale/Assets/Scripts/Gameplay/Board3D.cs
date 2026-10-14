using System.Collections.Generic;
using CrushRoyale.Core.Board;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Gameplay
{
    /// <summary>
    /// Labo 3D : les gemmes taillees en volume, a partir de la meme forme que leur version peinte.
    ///
    /// Chaque couleur garde la silhouette qu'elle a en 2D, rond, losange, octogone, etoile, hexagone, triangle,
    /// pour que le jeu reste lisible sans la couleur. Le contour sert de rondiste ; au-dessus, une table plus petite
    /// et une couronne de facettes ; au-dessous, un pavillon qui se ferme en pointe. Chaque triangle a ses propres
    /// sommets, donc sa propre normale : c'est l'ombrage plat qui fait les facettes, sans aucune texture.
    /// </summary>
    public static class GemMeshes
    {
        private static readonly Dictionary<PieceColor, Mesh> Cache = new Dictionary<PieceColor, Mesh>();

        public static Mesh For(PieceColor color)
        {
            if (Cache.TryGetValue(color, out Mesh mesh) && mesh != null)
            {
                return mesh;
            }
            mesh = Build(Outline(color));
            mesh.name = "Gem3D_" + color;
            return Cache[color] = mesh;
        }

        private static Vector2[] Outline(PieceColor color)
        {
            switch (color)
            {
                case PieceColor.Red: return Polygon(16, 1f, 0f);
                case PieceColor.Blue:
                    return new[] { new Vector2(0f, 1.15f), new Vector2(0.78f, 0f), new Vector2(0f, -1.15f), new Vector2(-0.78f, 0f) };
                case PieceColor.Green: return Emerald(0.88f, 0.3f);
                case PieceColor.Yellow: return Star(5, 1.08f, 0.5f);
                case PieceColor.Purple: return Polygon(6, 1.02f, 90f);
                case PieceColor.Orange:
                    {
                        Vector2[] tri = Polygon(3, 1.18f, 90f);
                        for (int i = 0; i < tri.Length; i++)
                        {
                            tri[i].y -= 0.18f;
                        }
                        return tri;
                    }
                default: return Polygon(16, 1f, 0f);
            }
        }

        private static Vector2[] Polygon(int sides, float radius, float startDeg)
        {
            var points = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = (startDeg - i * 360f / sides) * Mathf.Deg2Rad;
                points[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
            }
            return points;
        }

        private static Vector2[] Star(int tips, float outer, float inner)
        {
            var points = new Vector2[tips * 2];
            for (int i = 0; i < points.Length; i++)
            {
                float a = (90f - i * 180f / tips) * Mathf.Deg2Rad;
                float r = i % 2 == 0 ? outer : inner;
                points[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            return points;
        }

        /// <summary>Carre a coins coupes, la taille emeraude.</summary>
        private static Vector2[] Emerald(float half, float cut)
        {
            return new[]
            {
                new Vector2(-half + cut, half), new Vector2(half - cut, half), new Vector2(half, half - cut),
                new Vector2(half, -half + cut), new Vector2(half - cut, -half), new Vector2(-half + cut, -half),
                new Vector2(-half, -half + cut), new Vector2(-half, half - cut)
            };
        }

        private static Mesh Build(Vector2[] outline)
        {
            // La camera regarde vers +Z : le dessus de la gemme, la table, est donc du cote des Z negatifs.
            const float crown = 0.38f;
            const float table = 0.56f;
            const float girdle = 0.05f;
            const float pavilion = 0.62f;

            int n = outline.Length;
            var top = new Vector3[n];
            var upper = new Vector3[n];
            var lower = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                top[i] = new Vector3(outline[i].x * table, outline[i].y * table, -crown);
                upper[i] = new Vector3(outline[i].x, outline[i].y, -girdle);
                lower[i] = new Vector3(outline[i].x, outline[i].y, girdle);
            }
            var culet = new Vector3(0f, 0f, pavilion);
            var tableCenter = new Vector3(0f, 0f, -crown);

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();

            // Le cerclage d'or : une collerette plate autour du rondiste, tournee vers la camera, comme le liseré
            // dore de chaque gemme peinte. Le canal rouge de la couleur de sommet dit au shader « ceci est de l'or ».
            const float bezel = 1.14f;
            const float bezelDepth = 0.02f;
            var rimInner = new Vector3[n];
            var rimOuter = new Vector3[n];
            var rimBack = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                rimInner[i] = new Vector3(outline[i].x, outline[i].y, -girdle - bezelDepth);
                rimOuter[i] = new Vector3(outline[i].x * bezel, outline[i].y * bezel, -girdle - bezelDepth);
                rimBack[i] = new Vector3(outline[i].x * bezel, outline[i].y * bezel, girdle);
            }

            bool gold = false;

            void Face(Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                // Toujours vers l'exterieur : le centre de la gemme est a l'origine.
                if (Vector3.Dot(normal, (a + b + c) / 3f) < 0f)
                {
                    normal = -normal;
                }
                int start = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                normals.Add(normal);
                normals.Add(normal);
                normals.Add(normal);
                Color tag = gold ? Color.red : Color.black;
                colors.Add(tag);
                colors.Add(tag);
                colors.Add(tag);
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
            }

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Face(tableCenter, top[i], top[j]);
                // La couronne en deux triangles par cote : deux facettes, deux occasions de scintiller.
                Face(top[i], upper[i], upper[j]);
                Face(top[i], upper[j], top[j]);
                Face(upper[i], lower[i], lower[j]);
                Face(upper[i], lower[j], upper[j]);
                Face(lower[i], culet, lower[j]);
            }

            gold = true;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Face(rimInner[i], rimOuter[i], rimOuter[j]);
                Face(rimInner[i], rimOuter[j], rimInner[j]);
                Face(rimOuter[i], rimBack[i], rimBack[j]);
                Face(rimOuter[i], rimBack[j], rimOuter[j]);
            }

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>
    /// Labo 3D : rend le plateau en volume sans toucher a une ligne de sa logique.
    ///
    /// Le plateau 2D continue de tout faire : il echange, fait tomber, ecrase, rebondit, fait disparaitre. Ce
    /// composant ne fait que copier, a chaque image, la position, l'echelle et l'opacite de chaque gemme 2D sur sa
    /// jumelle 3D, et eteindre le corps peint de la gemme 2D. Toutes les animations existantes passent en 3D d'un
    /// coup, et l'entree tactile reste exacte puisque la camera est orthographique et cadree sur le plateau.
    ///
    /// Les yeux, les rayures des speciales, le compte a rebours des bombes et les effets restent en 2D, au-dessus :
    /// le rendu 3D est insere sous le calque des gemmes, pas par-dessus.
    /// </summary>
    public sealed class Board3D : MonoBehaviour
    {
        private const int Layer = 31;

        /// <summary>Le monde 3D est range loin de tout, pour que rien d'autre ne le voie par accident.</summary>
        private static readonly Vector3 WorldOrigin = new Vector3(5000f, 5000f, 0f);

        private BoardView _board;
        private RectTransform _pieces;
        private Camera _camera;
        private RenderTexture _target;
        private RawImage _screen;
        private Shader _shader;

        private readonly Dictionary<PieceView, Twin> _twins = new Dictionary<PieceView, Twin>();
        private readonly List<PieceView> _gone = new List<PieceView>();

        private sealed class Twin
        {
            public GameObject Go;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock Block;
            public PieceColor Color;
            public Vector3 LastPosition;
            public float Spin;
            public float Phase;
        }

        public static Board3D Attach(BoardView board, RectTransform piecesLayer)
        {
            Board3D lab = board.gameObject.AddComponent<Board3D>();
            lab._board = board;
            lab._pieces = piecesLayer;
            return lab;
        }

        private void Start()
        {
            _shader = Resources.Load<Shader>("Lab3D/Gem3D");
            if (_shader == null)
            {
                Debug.LogWarning("Board3D: shader introuvable, le plateau reste en 2D.");
                enabled = false;
                return;
            }

            // L'image qui affiche le rendu, glissee juste sous les gemmes 2D.
            RectTransform host = (RectTransform)_pieces.parent;
            RectTransform screenRect = UIFactory.Stretch(UIFactory.Rect("Board3D", host));
            screenRect.SetSiblingIndex(_pieces.GetSiblingIndex());
            _screen = screenRect.gameObject.AddComponent<RawImage>();
            _screen.raycastTarget = false;

            var cameraGo = new GameObject("Board3DCamera");
            cameraGo.transform.SetParent(transform, false);
            _camera = cameraGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _camera.cullingMask = 1 << Layer;
            _camera.nearClipPlane = 1f;
            _camera.farClipPlane = 400f;
            _camera.enabled = false;
        }

        private void LateUpdate()
        {
            if (_shader == null || _board == null)
            {
                return;
            }
            if (_board.IsGhost)
            {
                // Le petit plateau de l'adversaire reste en 2D : il ne vaut pas une deuxieme camera.
                enabled = false;
                return;
            }

            Rect rect = _pieces.rect;
            if (rect.width < 10f || rect.height < 10f)
            {
                return;
            }
            EnsureTarget(rect);

            // La camera cadre exactement le calque des gemmes : une unite du monde 3D est un point du canevas.
            Vector3 center = WorldOrigin + new Vector3(rect.center.x, rect.center.y, 0f);
            _camera.transform.position = center + new Vector3(0f, 0f, -200f);
            _camera.transform.rotation = Quaternion.identity;
            _camera.orthographicSize = rect.height * 0.5f;
            _camera.aspect = rect.width / rect.height;

            _gone.Clear();
            foreach (PieceView view in _twins.Keys)
            {
                if (view == null)
                {
                    _gone.Add(view);
                }
            }
            foreach (PieceView view in _gone)
            {
                Destroy(_twins[view].Go);
                _twins.Remove(view);
            }

            for (int i = 0; i < _pieces.childCount; i++)
            {
                PieceView view = _pieces.GetChild(i).GetComponent<PieceView>();
                if (view == null || view.Piece.IsEmpty)
                {
                    continue;
                }
                Sync(view);
            }

            _camera.Render();
        }

        private void EnsureTarget(Rect rect)
        {
            int width = Mathf.Clamp(Mathf.RoundToInt(rect.width), 64, 2048);
            int height = Mathf.Clamp(Mathf.RoundToInt(rect.height), 64, 2048);
            if (_target != null && _target.width == width && _target.height == height)
            {
                return;
            }
            if (_target != null)
            {
                _target.Release();
                Destroy(_target);
            }
            _target = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "Board3D" };
            _target.Create();
            _camera.targetTexture = _target;
            _screen.texture = _target;
        }

        private void Sync(PieceView view)
        {
            Piece piece = view.Piece;
            bool volume = piece.IsMatchable && piece.Color != PieceColor.None;
            view.SetBodyVisible(!volume);

            if (!_twins.TryGetValue(view, out Twin twin))
            {
                if (!volume)
                {
                    return;
                }
                twin = Create(view);
                _twins[view] = twin;
            }
            twin.Go.SetActive(volume && view.gameObject.activeInHierarchy);
            if (!volume)
            {
                return;
            }
            if (twin.Color != piece.Color)
            {
                twin.Color = piece.Color;
                twin.Filter.sharedMesh = GemMeshes.For(piece.Color);
            }

            Vector3 local = view.Rect.localPosition;
            Vector3 position = WorldOrigin + new Vector3(local.x, local.y, 0f);

            // Le mouvement fait tourner la gemme : elle roule en tombant, pivote en s'echangeant, puis revient
            // doucement a sa pose. C'est la principale chose que le 2D ne pouvait pas montrer.
            Vector3 moved = position - twin.LastPosition;
            twin.LastPosition = position;
            float cell = Mathf.Max(1f, _board.Cell);
            if (moved.sqrMagnitude < cell * cell)
            {
                twin.Spin += moved.y * 160f / cell - moved.x * 90f / cell;
            }
            twin.Spin = Mathf.Lerp(twin.Spin, 0f, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));

            float sway = Mathf.Sin(Time.unscaledTime * 1.3f + twin.Phase) * 7f;
            twin.Go.transform.position = position;
            twin.Go.transform.rotation = Quaternion.Euler(-22f + twin.Spin, 14f + sway, 0f);

            Vector3 scale = view.Rect.localScale;
            float radius = view.Rect.sizeDelta.x * 0.46f;
            twin.Go.transform.localScale = new Vector3(radius * scale.x, radius * scale.y, radius * (scale.x + scale.y) * 0.5f);

            twin.Renderer.GetPropertyBlock(twin.Block);
            twin.Block.SetColor("_Color", Tint(piece.Color));
            twin.Block.SetFloat("_Alpha", view.Alpha);
            twin.Block.SetFloat("_Glow", piece.IsSpecial ? 1f : 0f);
            twin.Renderer.SetPropertyBlock(twin.Block);
        }

        private Twin Create(PieceView view)
        {
            var go = new GameObject("Gem3D");
            go.layer = Layer;
            go.transform.SetParent(transform, false);
            var twin = new Twin
            {
                Go = go,
                Filter = go.AddComponent<MeshFilter>(),
                Renderer = go.AddComponent<MeshRenderer>(),
                Block = new MaterialPropertyBlock(),
                Color = PieceColor.None,
                Phase = Random.value * 10f
            };
            twin.Renderer.sharedMaterial = SharedMaterial();
            twin.Renderer.SetPropertyBlock(twin.Block);
            twin.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            twin.Renderer.receiveShadows = false;
            twin.LastPosition = WorldOrigin + view.Rect.localPosition;
            return twin;
        }

        private Material _material;

        private Material SharedMaterial()
        {
            if (_material == null)
            {
                _material = new Material(_shader) { name = "Gem3D" };
                _material.SetFloat("_Seed", Random.value * 100f);
            }
            return _material;
        }

        /// <summary>Les couleurs du plateau, un peu plus franches : l'ombrage en mange une partie.</summary>
        private static Color Tint(PieceColor color)
        {
            switch (color)
            {
                case PieceColor.Red: return new Color(0.95f, 0.16f, 0.24f);
                case PieceColor.Blue: return new Color(0.2f, 0.52f, 1f);
                case PieceColor.Green: return new Color(0.18f, 0.86f, 0.42f);
                case PieceColor.Yellow: return new Color(1f, 0.84f, 0.16f);
                case PieceColor.Purple: return new Color(0.68f, 0.3f, 0.98f);
                case PieceColor.Orange: return new Color(1f, 0.52f, 0.12f);
                default: return Color.white;
            }
        }

        private void OnDestroy()
        {
            if (_target != null)
            {
                _target.Release();
                Destroy(_target);
            }
            if (_material != null)
            {
                Destroy(_material);
            }
        }
    }
}
