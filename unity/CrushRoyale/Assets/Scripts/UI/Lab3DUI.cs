using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CrushRoyale.Game.UI
{
    /// <summary>
    /// Labo 3D : toute l'interface passe dans un espace en perspective, sans qu'aucun ecran soit reecrit.
    ///
    /// Le canevas etait projete a plat par-dessus l'image. Il est maintenant vu par une camera en perspective,
    /// ce qui suffit a donner du relief a tout ce qui sort du plan : un bouton qui s'enfonce, une carte qui
    /// s'incline, une page qui entre en pivotant, un fond pose loin derriere. Trois profondeurs sont fixees une
    /// fois pour tout le jeu : le decor au fond, les panneaux au milieu, les boutons devant. Comme la scene des
    /// ecrans s'incline un peu avec le telephone, ces trois plans glissent les uns sur les autres.
    /// </summary>
    public static class Lab3DUI
    {
        /// <summary>Distance du canevas a la camera, en unites du canevas. Toutes les profondeurs s'y rapportent.</summary>
        public const float PlaneDistance = 900f;

        /// <summary>Le decor recule de cette distance : assez pour glisser nettement, pas assez pour se deformer.</summary>
        public const float BackdropDepth = 520f;

        /// <summary>Les boutons avancent de ceci : un rien, mais ils passent devant tout le reste.</summary>
        public const float ButtonDepth = -18f;

        /// <summary>Les cartes, a peine devant les panneaux, pour qu'une carte se detache de la page.</summary>
        public const float CardDepth = -6f;

        public static Camera Camera { get; private set; }

        public static void Setup(Canvas canvas, RectTransform screens)
        {
            var cameraGo = new GameObject("UICamera3D");
            cameraGo.transform.SetParent(canvas.transform.parent, false);
            Camera = cameraGo.AddComponent<Camera>();
            Camera.orthographic = false;
            // Champ etroit : de la perspective, sans deformer les bords de l'ecran.
            Camera.fieldOfView = 30f;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = Color.black;
            Camera.cullingMask = 1 << 5;
            Camera.nearClipPlane = 10f;
            Camera.farClipPlane = PlaneDistance + BackdropDepth + 800f;

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = Camera;
            canvas.planeDistance = PlaneDistance;

            screens.gameObject.AddComponent<StageTilt>();
        }

        /// <summary>Echelle qui rend a un objet recule la taille apparente qu'il avait dans le plan.</summary>
        public static float Compensate(float depth) => (PlaneDistance + depth) / PlaneDistance;

        /// <summary>Pose un element a une profondeur donnee, en gardant sa taille apparente.</summary>
        public static void SetDepth(RectTransform rect, float depth, bool keepApparentSize)
        {
            Vector3 position = rect.localPosition;
            rect.localPosition = new Vector3(position.x, position.y, depth);
            if (keepApparentSize)
            {
                float k = Compensate(depth);
                rect.localScale = new Vector3(rect.localScale.x * k, rect.localScale.y * k, 1f);
            }
        }
    }

    /// <summary>
    /// Incline legerement toute la scene des ecrans selon la tenue du telephone.
    ///
    /// C'est ce qui rend les profondeurs visibles : sans mouvement, un decor recule a exactement l'air d'un decor
    /// plat. Trois degres au plus, lisses, pour que le plateau reste stable sous le doigt ; rien du tout en mode
    /// « animations reduites ».
    /// </summary>
    public sealed class StageTilt : MonoBehaviour
    {
        private const float MaxDegrees = 3f;
        private Vector2 _current;
        private Vector2 _neutral;
        private bool _calibrated;

        private void Update()
        {
            Vector2 target = Vector2.zero;
            if (!Transitions.ReduceMotion && SystemInfo.supportsAccelerometer)
            {
                Vector3 a = Input.acceleration;
                var reading = new Vector2(a.x, a.y);
                // La premiere lecture sert de neutre : le telephone tel qu'on le tient, pas a plat sur une table.
                if (!_calibrated)
                {
                    _neutral = reading;
                    _calibrated = true;
                }
                _neutral = Vector2.Lerp(_neutral, reading, 0.15f * Time.unscaledDeltaTime);
                Vector2 delta = reading - _neutral;
                target = new Vector2(Mathf.Clamp(delta.y, -1f, 1f), Mathf.Clamp(delta.x, -1f, 1f)) * (MaxDegrees * 2.5f);
                target = Vector2.ClampMagnitude(target, MaxDegrees);
            }
            _current = Vector2.Lerp(_current, target, 1f - Mathf.Exp(-5f * Time.unscaledDeltaTime));
            transform.localRotation = Quaternion.Euler(_current.x, -_current.y, 0f);
        }
    }

    /// <summary>
    /// Un bouton ou une carte qui reagit au doigt comme un objet : il s'incline du cote ou on appuie et s'enfonce,
    /// puis revient avec un leger depassement en relachant.
    /// </summary>
    public sealed class Tilt3D : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public float MaxAngle = 10f;
        public float Push = 16f;

        private RectTransform _rect;
        private Vector2 _target;
        private float _targetPush;
        private Vector2 _angle;
        private Vector2 _velocity;
        private float _push;
        private float _pushVelocity;
        private float _baseZ;
        private bool _hasBase;

        private void Awake()
        {
            _rect = (RectTransform)transform;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Transitions.ReduceMotion)
            {
                return;
            }
            if (!_hasBase)
            {
                _baseZ = _rect.localPosition.z;
                _hasBase = true;
            }
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, eventData.position, eventData.pressEventCamera, out Vector2 local))
            {
                return;
            }
            Rect r = _rect.rect;
            float nx = Mathf.Clamp((local.x - r.center.x) / Mathf.Max(1f, r.width * 0.5f), -1f, 1f);
            float ny = Mathf.Clamp((local.y - r.center.y) / Mathf.Max(1f, r.height * 0.5f), -1f, 1f);
            // Appuyer a droite enfonce le bord droit, appuyer en haut enfonce le haut : le doigt pousse la matiere.
            _target = new Vector2(ny * MaxAngle, -nx * MaxAngle);
            _targetPush = Push;
        }

        public void OnPointerUp(PointerEventData eventData) => Release();

        public void OnPointerExit(PointerEventData eventData) => Release();

        private void Release()
        {
            _target = Vector2.zero;
            _targetPush = 0f;
        }

        private void Update()
        {
            if (!_hasBase)
            {
                return;
            }
            // Un ressort amorti : il revient a plat en repassant un peu de l'autre cote, comme un objet reel.
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            const float stiffness = 420f;
            const float damping = 22f;
            _velocity += ((_target - _angle) * stiffness - _velocity * damping) * dt;
            _angle += _velocity * dt;
            _pushVelocity += ((_targetPush - _push) * stiffness - _pushVelocity * damping) * dt;
            _push += _pushVelocity * dt;

            bool settled = _target == Vector2.zero && _angle.sqrMagnitude < 0.0004f && _velocity.sqrMagnitude < 0.0004f
                           && Mathf.Abs(_push) < 0.02f && Mathf.Abs(_pushVelocity) < 0.02f;
            if (settled)
            {
                _angle = Vector2.zero;
                _velocity = Vector2.zero;
                _push = 0f;
                _pushVelocity = 0f;
            }
            _rect.localRotation = Quaternion.Euler(_angle.x, _angle.y, _rect.localEulerAngles.z);
            Vector3 p = _rect.localPosition;
            _rect.localPosition = new Vector3(p.x, p.y, _baseZ + _push);
        }

        /// <summary>Ajoute le comportement a un element, une seule fois.</summary>
        public static void On(Component target, float maxAngle, float push)
        {
            if (target == null || target.GetComponent<Tilt3D>() != null)
            {
                return;
            }
            Tilt3D tilt = target.gameObject.AddComponent<Tilt3D>();
            tilt.MaxAngle = maxAngle;
            tilt.Push = push;
        }
    }
}
