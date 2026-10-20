using UnityEngine;

namespace CrushRoyale.Game.UI
{
    /// <summary>
    /// Donne vie a un familier dessine une seule fois.
    ///
    /// Les pets n'ont qu'une image fixe, et la seule animation qu'ils avaient etait celle de tout le reste de
    /// l'interface : un gonflement regulier, le meme que celui d'un bouton. Un bouton qui respire a l'air
    /// vivant ; un animal qui ne fait que respirer a l'air empaille.
    ///
    /// Ce qui distingue un etre vivant d'un objet, c'est l'irregularite. Celui-ci se balance doucement en
    /// permanence, et de temps en temps, jamais au meme intervalle, il fait un bond : il s'accroupit, saute,
    /// s'ecrase un peu en retombant, et se remet en place. Entre deux bonds il penche la tete d'un cote.
    /// Rien de tout cela ne demande une image de plus.
    ///
    /// Chaque familier tire son propre rythme au demarrage, sinon une liste de pets saute en choeur, ce qui
    /// detruit exactement l'illusion recherchee.
    /// </summary>
    public sealed class PetIdle : MonoBehaviour
    {
        /// <summary>Hauteur du bond, en fraction de la taille du familier.</summary>
        public float HopHeight = 0.12f;

        /// <summary>Amplitude du balancement permanent, en degres.</summary>
        public float SwayDegrees = 3.5f;

        private RectTransform _rect;
        private Vector2 _home;
        private float _swayPhase;
        private float _breathPhase;
        private float _nextHop;
        private float _hopStarted = -1f;
        private float _hopDuration;
        private float _size;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _home = _rect.anchoredPosition;
            _swayPhase = Random.value * 6.28f;
            _breathPhase = Random.value * 6.28f;
            ScheduleHop();
            if (GameRoot.Instance != null && GameRoot.Instance.Save.Settings.ReduceMotion)
            {
                enabled = false;
            }
        }

        private void OnEnable()
        {
            // La taille n'est connue qu'une fois la mise en page faite, et elle change avec l'ecran.
            _size = Mathf.Max(1f, _rect.rect.height);
        }

        private void ScheduleHop()
        {
            // Jamais le meme intervalle : un bond toutes les trois secondes pile serait une horloge, pas un animal.
            _nextHop = Time.unscaledTime + Random.Range(2.6f, 6.5f);
            _hopDuration = Random.Range(0.52f, 0.68f);
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (_size <= 1f)
            {
                _size = Mathf.Max(1f, _rect.rect.height);
            }

            if (_hopStarted < 0f && now >= _nextHop)
            {
                _hopStarted = now;
            }

            float lift = 0f;
            float squashX = 1f;
            float squashY = 1f;
            if (_hopStarted >= 0f)
            {
                float k = (now - _hopStarted) / _hopDuration;
                if (k >= 1f)
                {
                    _hopStarted = -1f;
                    ScheduleHop();
                }
                else
                {
                    // Trois temps : il se ramasse, il part, il encaisse en retombant. C'est l'accroupissement
                    // qui annonce le saut, et l'ecrasement a l'arrivee qui lui donne du poids.
                    if (k < 0.18f)
                    {
                        float a = k / 0.18f;
                        squashY = 1f - 0.14f * a;
                        squashX = 1f + 0.10f * a;
                    }
                    else if (k < 0.78f)
                    {
                        float a = (k - 0.18f) / 0.6f;
                        // Une parabole : vite en montant, suspendu en haut, vite en redescendant.
                        lift = Mathf.Sin(a * Mathf.PI) * HopHeight * _size;
                        squashY = 1f + 0.08f * Mathf.Sin(a * Mathf.PI);
                        squashX = 1f - 0.06f * Mathf.Sin(a * Mathf.PI);
                    }
                    else
                    {
                        float a = (k - 0.78f) / 0.22f;
                        squashY = 1f - 0.16f * (1f - a) * (1f - a);
                        squashX = 1f + 0.12f * (1f - a) * (1f - a);
                    }
                }
            }

            // Hors du bond, il respire et se balance, deux rythmes differents pour qu'ils ne se superposent pas.
            float breath = Mathf.Sin(now * 1.5f + _breathPhase) * 0.018f;
            float sway = Mathf.Sin(now * 0.9f + _swayPhase) * SwayDegrees;

            _rect.anchoredPosition = new Vector2(_home.x, _home.y + lift);
            _rect.localScale = new Vector3(squashX * (1f - breath * 0.4f), squashY * (1f + breath), 1f);
            _rect.localRotation = Quaternion.Euler(0f, 0f, sway);
        }

        /// <summary>Ajoute le comportement a une image de familier, une seule fois.</summary>
        public static void On(Component target, float hopHeight = 0.12f, float swayDegrees = 3.5f)
        {
            if (target == null || target.GetComponent<PetIdle>() != null)
            {
                return;
            }
            // Deux animations qui ecrivent la meme echelle se battraient : celle-ci remplace la respiration.
            Breathe breathe = target.GetComponent<Breathe>();
            if (breathe != null)
            {
                Destroy(breathe);
            }
            PetIdle idle = target.gameObject.AddComponent<PetIdle>();
            idle.HopHeight = hopHeight;
            idle.SwayDegrees = swayDegrees;
        }
    }
}
