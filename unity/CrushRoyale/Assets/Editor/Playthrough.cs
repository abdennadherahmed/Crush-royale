using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Story;
using CrushRoyale.Game;
using CrushRoyale.Game.Gameplay;
using CrushRoyale.Game.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.EditorTools
{
    /// <summary>
    /// Joue le jeu au lieu de le photographier.
    ///
    /// Une capture montre un ecran a l'arret. Elle ne dit pas si un bouton repond, s'il mene quelque part, ni s'il
    /// jette une exception au deuxieme appui. Ces defauts ne se voyaient donc qu'apres un build de cinquante minutes,
    /// sur un telephone, et seulement si quelqu'un appuyait au bon endroit. C'est ce trou que cet outil ferme : il
    /// ouvre chaque ecran, appuie sur chacun de ses boutons, et joue une partie entiere du debut a la fin.
    ///
    /// Chaque bouton est essaye sur un exemplaire neuf de l'ecran, parce qu'un bouton qui detruit sa page empecherait
    /// d'essayer les suivants, et parce qu'un enchainement d'appuis ne dirait plus lequel a casse quoi.
    ///
    ///     Unity -batchmode -quit -projectPath . -executeMethod CrushRoyale.EditorTools.Playthrough.Run
    ///         -playthroughOutput &lt;dossier&gt;
    /// </summary>
    public static class Playthrough
    {
        /// <summary>Au-dela, l'ecran a une liste et non un menu : les premiers suffisent a trouver un defaut.</summary>
        private const int MaxButtonsPerScreen = 40;

        private sealed class Step
        {
            public string Screen;
            public string Button;
            public string Outcome;
            public string Detail;
        }

        [MenuItem("Crush Royale/Jouer le jeu (rapport)")]
        public static void RunFromMenu() => Execute(Path.GetFullPath("playthrough"));

        /// <summary>
        /// Joue les etapes demandees, avec le compte releve, et dit ce que chacune a donne.
        ///
        ///     Unity -batchmode -quit -projectPath . -executeMethod CrushRoyale.EditorTools.Playthrough.PlayStages
        ///         -stages 164,165 -playthroughOutput &lt;dossier&gt;
        ///
        /// Le bot joue avec l'equipement du compte, sur le plateau exact de l'etape, avec les memes regles que
        /// le telephone : le moteur est partage, c'est celui que le serveur rejoue pour verifier un score.
        /// </summary>
        public static void PlayStages()
        {
            string list = Argument("-stages") ?? "1";
            string output = Path.GetFullPath(Argument("-playthroughOutput") ?? "playthrough");
            Directory.CreateDirectory(output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameRoot game = OfflineHarness.BootGame();
            OfflineHarness.SetProfile(game, true);

            var steps = new List<Step>();
            int problems = 0;
            foreach (string piece in list.Split(','))
            {
                if (!int.TryParse(piece.Trim(), out int stageId))
                {
                    continue;
                }
                problems += PlayStage(game, stageId, steps);
            }
            OfflineHarness.Shutdown(game);
            WriteReport(output, steps, problems);
            Debug.Log("Playthrough: " + steps.Count + " etape(s) jouee(s), " + problems + " probleme(s), rapport dans " + output);
        }

        /// <summary>Joue une etape jusqu'au bout et resume ce qu'elle demandait et ce qui a ete fait.</summary>
        private static int PlayStage(GameRoot game, int stageId, List<Step> steps)
        {
            var step = new Step { Screen = "Etape " + stageId };
            try
            {
                MatchLaunch launch = MatchLaunch.OfflineStory(stageId);
                // L'equipement du compte, pas un plateau nu : des power-ups changent ce qu'une etape demande.
                Dictionary<string, int> owned = game.Backend.Profile?.Inventory?.PowerUps;
                if (owned != null)
                {
                    GameBalance balance = game.Backend.Balance;
                    // Les memes limites qu'un joueur : un nombre de places, et certains power-ups qui ne
                    // s'emportent qu'une fois. Les ignorer faisait refuser la partie avant de commencer.
                    foreach (KeyValuePair<string, int> entry in owned)
                    {
                        if (launch.OfflineLoadout.Count >= balance.PowerUps.LoadoutSlots)
                        {
                            break;
                        }
                        if (entry.Value <= 0 || !Enum.TryParse(entry.Key, true, out PowerUpType type))
                        {
                            continue;
                        }
                        PowerUpDefinition def = balance.PowerUps.Get(type);
                        // Une partie hors ligne est jouee en Bronze : un power-up debloque plus haut serait
                        // refuse, exactement comme pour un joueur qui n'y est pas encore arrive.
                        if (def.PvpOnly || def.UnlockLeague > League.Bronze)
                        {
                            continue;
                        }
                        launch.OfflineLoadout.Add(new LoadoutEntry(type, def.OncePerMatch ? 1 : entry.Value));
                    }
                }
                SessionConfig config = launch.BuildConfig(game.Backend);
                var session = new GameSession(config, game.Backend.Balance, game.Backend.PlayerId);
                StageResult result = HeadlessRunner.Run(session, new GreedyBot());
                step.Button = Objectives(config);
                step.Outcome = result.State.ToString();
                step.Detail = "score " + result.FinalScore + ", " + result.Stars + " etoile(s), "
                              + result.MovesUsed + " coup(s)";
                steps.Add(step);
                // Perdre n'est pas un defaut : une etape difficile se perd, et c'est le bot qui joue, pas un
                // joueur. Seule une partie qui ne se termine pas du tout signale un probleme.
                return 0;
            }
            catch (Exception ex)
            {
                step.Outcome = "exception";
                step.Detail = ex.GetBaseException().Message;
                steps.Add(step);
                return 1;
            }
        }

        /// <summary>Ce que l'etape demande, en clair, pour que le resultat se lise sans ouvrir le jeu.</summary>
        private static string Objectives(SessionConfig config)
        {
            var text = new StringBuilder();
            foreach (StageObjective objective in config.Objectives ?? new List<StageObjective>())
            {
                if (text.Length > 0)
                {
                    text.Append(" + ");
                }
                text.Append(objective.Type).Append(' ').Append(objective.Target);
            }
            return text.Length > 0 ? text.ToString() : "sans objectif";
        }

        public static void Run()
        {
            string output = Path.GetFullPath(Argument("-playthroughOutput") ?? "playthrough");
            int problems = Execute(output);
            // Un bouton qui jette est exactement ce que cet outil cherche : il doit faire echouer le build.
            if (problems > 0 && Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }

        private static int Execute(string output)
        {
            Directory.CreateDirectory(output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameRoot game = OfflineHarness.BootGame();
            var steps = new List<Step>();
            int problems = 0;

            foreach (ScreenCase test in OfflineHarness.Screens())
            {
                problems += PressEveryButton(game, test, steps);
            }
            problems += PlayOneStage(game, steps);

            OfflineHarness.Shutdown(game);
            WriteReport(output, steps, problems);
            Debug.Log("Playthrough: " + steps.Count + " appui(s), " + problems + " probleme(s), rapport dans " + output);
            return problems;
        }

        /// <summary>Ouvre l'ecran une fois par bouton, appuie, et note ce qui s'est passe.</summary>
        private static int PressEveryButton(GameRoot game, ScreenCase test, List<Step> steps)
        {
            int count = CountButtons(game, test);
            int problems = 0;
            for (int i = 0; i < Mathf.Min(count, MaxButtonsPerScreen); i++)
            {
                var step = new Step { Screen = test.Name };
                RectTransform host = null;
                try
                {
                    host = Build(game, test);
                    List<Button> buttons = Buttons(host);
                    if (i >= buttons.Count)
                    {
                        break;
                    }
                    Button button = buttons[i];
                    step.Button = Describe(button);
                    if (!button.interactable)
                    {
                        // Un bouton grise est un etat voulu (verrouille, deja reclame), pas un defaut.
                        step.Outcome = "grise";
                        steps.Add(step);
                        continue;
                    }
                    string before = Fingerprint(host);
                    button.onClick.Invoke();
                    Canvas.ForceUpdateCanvases();
                    step.Outcome = "ok";
                    step.Detail = WhereItLed(game, host, before);
                }
                catch (Exception ex)
                {
                    step.Outcome = "exception";
                    step.Detail = ex.GetBaseException().Message;
                    problems++;
                }
                finally
                {
                    if (host != null)
                    {
                        UnityEngine.Object.DestroyImmediate(host.gameObject);
                    }
                    ClearOverlays(game);
                    game.UI.ResetForTooling();
                }
                steps.Add(step);
            }
            return problems;
        }

        /// <summary>
        /// Ou l'appui a mene : une page, une fenetre, un message, ou nulle part.
        ///
        /// « nulle part » est la trouvaille interessante : un bouton qui ne fait rien ressemble, sur une photo,
        /// exactement a un bouton qui marche. C'est le genre de defaut qu'un joueur signale par « j'appuie et il
        /// ne se passe rien », sans pouvoir dire lequel.
        /// </summary>
        private static string WhereItLed(GameRoot game, RectTransform host, string before)
        {
            if (game.UI.Current != null)
            {
                return "ouvre " + game.UI.Current.GetType().Name;
            }
            if (game.UI.DialogLayer != null && game.UI.DialogLayer.childCount > 0)
            {
                return "ouvre une fenetre";
            }
            if (game.UI.ToastLayer != null && game.UI.ToastLayer.childCount > 0)
            {
                return "affiche un message";
            }
            // Un onglet, un tri, une case cochee ne changent pas de page : ils refont la page sur place. Les
            // confondre avec un bouton mort remplirait le rapport de faux signalements.
            return Fingerprint(host) != before ? "met la page a jour" : "ne mene nulle part";
        }

        /// <summary>Ce que la page montre, resume : de quoi voir qu'un appui l'a changee.</summary>
        private static string Fingerprint(RectTransform host)
        {
            var text = new StringBuilder();
            foreach (Text label in host.GetComponentsInChildren<Text>(true))
            {
                text.Append(label.text).Append('|');
            }
            foreach (Image image in host.GetComponentsInChildren<Image>(true))
            {
                text.Append(image.sprite != null ? image.sprite.name : "-").Append(image.color.ToString()).Append('|');
            }
            return text.ToString();
        }

        private static int CountButtons(GameRoot game, ScreenCase test)
        {
            RectTransform host = null;
            try
            {
                host = Build(game, test);
                return Buttons(host).Count;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Playthrough: " + test.Name + " ne s'ouvre pas : " + ex.GetBaseException().Message);
                return 0;
            }
            finally
            {
                if (host != null)
                {
                    UnityEngine.Object.DestroyImmediate(host.gameObject);
                }
                ClearOverlays(game);
            }
        }

        private static RectTransform Build(GameRoot game, ScreenCase test)
        {
            OfflineHarness.SetProfile(game, test.WithProfile);
            RectTransform host = UIFactory.Stretch(UIFactory.Rect(test.Name, game.UI.Canvas.transform));
            host.SetSiblingIndex(0);
            object args = test.Args != null ? test.Args(game) : null;
            var screen = (UIScreen)host.gameObject.AddComponent(test.Type);
            screen.Setup(game.UI, args);
            // Sans ca l'ecran reste sur sa premiere image, celle ou il n'a encore rien charge, et la plupart de
            // ses boutons n'existent pas : ce sont justement ceux que les joueurs touchent.
            System.Threading.Tasks.Task shown = screen.OnShownAsync();
            if (shown != null && shown.IsFaulted)
            {
                throw shown.Exception.GetBaseException();
            }
            Canvas.ForceUpdateCanvases();
            return host;
        }

        private static List<Button> Buttons(RectTransform host) =>
            host.GetComponentsInChildren<Button>(true).Where(b => b != null && b.gameObject.activeInHierarchy).ToList();

        /// <summary>Le texte du bouton quand il en a un, sinon le nom de son objet : de quoi le retrouver.</summary>
        private static string Describe(Button button)
        {
            Text label = button.GetComponentInChildren<Text>(true);
            string text = label != null ? label.text : null;
            if (string.IsNullOrWhiteSpace(text))
            {
                // La plupart des boutons du jeu sont des icones sans texte, et « Button » ne dit a personne lequel
                // a casse. Le nom de l'image, puis celui du parent, les distinguent : « Shop », « item_crown ».
                Image icon = button.GetComponentsInChildren<Image>(true)
                    .FirstOrDefault(i => i.sprite != null && i.gameObject != button.gameObject);
                if (icon != null)
                {
                    return icon.sprite.name;
                }
                Transform parent = button.transform.parent;
                return parent != null ? parent.name + "/" + button.name : button.name;
            }
            text = text.Replace("\n", " ").Trim();
            return text.Length > 40 ? text.Substring(0, 40) + "..." : text;
        }

        /// <summary>Un appui ouvre souvent une fenetre ou un message : ils sont retires avant l'appui suivant.</summary>
        private static void ClearOverlays(GameRoot game)
        {
            Clear(game.UI.DialogLayer);
            Clear(game.UI.ToastLayer);
        }

        private static void Clear(Transform layer)
        {
            if (layer == null)
            {
                return;
            }
            for (int i = layer.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(layer.GetChild(i).gameObject);
            }
        }

        /// <summary>
        /// Joue reellement une etape, coup par coup, avec le meme moteur que le telephone et le bot du serveur.
        ///
        /// C'est la seule maniere de verifier qu'une partie se termine : le plateau se bloque, les combos
        /// s'enchainent, le score monte, et rien de tout cela n'est visible sur une photo du plateau au depart.
        /// </summary>
        private static int PlayOneStage(GameRoot game, List<Step> steps)
        {
            var step = new Step { Screen = "Partie", Button = "etape 1 jouee par le bot" };
            try
            {
                MatchLaunch launch = MatchLaunch.OfflineStory(1);
                var session = new GameSession(launch.BuildConfig(game.Backend), game.Backend.Balance, game.Backend.PlayerId);
                StageResult result = HeadlessRunner.Run(session, new GreedyBot());
                step.Outcome = "ok";
                step.Detail = "score " + result.FinalScore + ", " + result.State;
                steps.Add(step);
                return 0;
            }
            catch (Exception ex)
            {
                step.Outcome = "exception";
                step.Detail = ex.GetBaseException().Message;
                steps.Add(step);
                return 1;
            }
        }

        private static void WriteReport(string output, List<Step> steps, int problems)
        {
            var text = new StringBuilder();
            text.AppendLine("# Partie jouee par le harnais");
            text.AppendLine();
            text.AppendLine(steps.Count + " appui(s), " + problems + " probleme(s).");
            text.AppendLine();
            text.AppendLine("Le harnais joue hors ligne. Tout ce qui demande le serveur (chercher une partie, creer");
            text.AppendLine("une guilde, acheter) ne peut donc mener nulle part, et le retour n'a pas de page ou");
            text.AppendLine("revenir. Ce sont les autres « ne mene nulle part » qui meritent d'etre regardes.");
            text.AppendLine();
            foreach (IGrouping<string, Step> screen in steps.GroupBy(s => s.Screen))
            {
                text.AppendLine("## " + screen.Key);
                foreach (Step step in screen)
                {
                    text.Append("- ").Append(step.Button).Append(" : ").Append(step.Outcome);
                    if (!string.IsNullOrEmpty(step.Detail))
                    {
                        text.Append(" (").Append(step.Detail).Append(')');
                    }
                    text.AppendLine();
                }
                text.AppendLine();
            }
            File.WriteAllText(Path.Combine(output, "playthrough.md"), text.ToString());
        }

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }
}
