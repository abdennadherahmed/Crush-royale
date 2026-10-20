using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CrushRoyale.Core.Gameplay;
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
                    button.onClick.Invoke();
                    step.Outcome = "ok";
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
                }
                steps.Add(step);
            }
            return problems;
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
