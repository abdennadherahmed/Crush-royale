using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CrushRoyale.Contracts;
using CrushRoyale.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CrushRoyale.EditorTools
{
    /// <summary>
    /// Joue des actions en ligne, pour de vrai, contre un serveur.
    ///
    /// C'etait le dernier trou de l'outillage. Les captures montrent des ecrans, le harnais appuie sur leurs
    /// boutons, mais tout cela se passait hors ligne : se connecter, chercher une partie, envoyer une vie,
    /// rejoindre une guilde, acheter, rien de tout cela ne pouvait etre essaye ailleurs que sur un telephone,
    /// apres un build de cinquante minutes. La raison etait bete : l'authentification passe par un compte
    /// Google, qu'un PC de developpement n'a pas.
    ///
    /// Un serveur lance en Development accepte l'entete X-Dev-User a la place du jeton, comme pour ses propres
    /// tests. Le jeu demarre donc ici en pointant sur ce serveur, se connecte reellement, et enchaine des
    /// appels dont les reponses viennent du vrai code serveur.
    ///
    ///     powershell -ExecutionPolicy Bypass -File tools/local-server.ps1
    ///     Unity -batchmode -quit -projectPath . -executeMethod CrushRoyale.EditorTools.OnlineRun.Run
    ///         -onlineServer http://localhost:5199 -onlineOutput &lt;dossier&gt;
    /// </summary>
    public static class OnlineRun
    {
        private sealed class Step
        {
            public string Name;
            public string Outcome;
            public string Detail;
        }

        public static void Run()
        {
            string server = Argument("-onlineServer") ?? "http://localhost:5199";
            string output = Path.GetFullPath(Argument("-onlineOutput") ?? "online");
            Directory.CreateDirectory(output);

            // Un joueur neuf a chaque passage : un essai ne depend ainsi jamais de ce qu'a laisse le precedent.
            string player = Guid.NewGuid().ToString();
            GameRoot.ConfigOverride = config =>
            {
                config.ApiBaseUrl = server.TrimEnd('/');
                config.DevUserId = player;
            };
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameRoot booted = OfflineHarness.BootGame();
            // Lu ici, sur le fil principal, pendant qu'on y est encore : la connexion en aura besoin plus loin.
            booted.Save.DeviceHash(booted.Config.DeviceSalt);

            // Les appels reseau partent hors du fil principal, volontairement. Attendre une tache depuis le fil
            // principal de l'editeur bloque ce fil, et les suites de ces taches veulent justement y revenir :
            // le tout se fige, ce qui s'est produit au premier essai.
            int problems = Task.Run(() => RunAsync(server, output)).GetAwaiter().GetResult();
            // Detruire le jeu touche des objets de scene : cela doit se faire ici, sur le fil principal.
            OfflineHarness.Shutdown(booted);
            if (problems > 0 && Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }

        private static async Task<int> RunAsync(string server, string output)
        {
            GameRoot game = GameRoot.Instance;
            var steps = new List<Step>();
            int problems = 0;

            try
            {
                problems += await Try(steps, "connexion", async () =>
                {
                    await game.Backend.StartAsync("fr");
                    if (!game.Backend.IsOnline)
                    {
                        throw new Exception("le serveur a repondu, mais la session n'est pas ouverte");
                    }
                    return "joueur " + game.Backend.PlayerId;
                });

                if (game.Backend.IsOnline)
                {
                    problems += await Try(steps, "profil", async () =>
                    {
                        ProfileDto profile = await game.Backend.Client.Api.GetMeAsync();
                        return profile.DisplayName + ", etape " + profile.Story?.HighestUnlockedStage
                               + ", " + profile.Wallet?.Coins + " pieces";
                    });

                    problems += await Try(steps, "boutique", async () =>
                    {
                        ShopResponse shop = await game.Backend.Client.Api.GetShopAsync();
                        return (shop.Items?.Count ?? 0) + " article(s)";
                    });

                    problems += await Try(steps, "quetes", async () =>
                    {
                        QuestsResponse quests = await game.Backend.Client.Api.GetQuestsAsync();
                        return (quests.Quests?.Count ?? 0) + " quete(s)";
                    });

                    problems += await Try(steps, "classement", async () =>
                    {
                        LeaderboardResponse board = await game.Backend.Client.Api.GetWeeklyLeaderboardAsync(null);
                        return (board.Entries?.Count ?? 0) + " ligne(s)";
                    });

                    problems += await Try(steps, "guildes", async () =>
                    {
                        GuildSearchResponse guilds = await game.Backend.Client.Api.SearchGuildsAsync(string.Empty);
                        return (guilds.Guilds?.Count ?? 0) + " guilde(s)";
                    });
                }
            }
            finally
            {
                GameRoot.ConfigOverride = null;
            }

            WriteReport(output, server, steps, problems);
            Debug.Log("OnlineRun: " + steps.Count + " action(s), " + problems + " probleme(s), rapport dans " + output);
            return problems;
        }

        private static async Task<int> Try(List<Step> steps, string name, Func<Task<string>> action)
        {
            var step = new Step { Name = name };
            try
            {
                step.Detail = await action();
                step.Outcome = "ok";
                steps.Add(step);
                return 0;
            }
            catch (Exception ex)
            {
                step.Outcome = "echec";
                step.Detail = ex.GetBaseException().Message;
                steps.Add(step);
                return 1;
            }
        }

        private static void WriteReport(string output, string server, List<Step> steps, int problems)
        {
            var text = new StringBuilder();
            text.AppendLine("# Actions jouees en ligne");
            text.AppendLine();
            text.AppendLine("Serveur : " + server);
            text.AppendLine(steps.Count + " action(s), " + problems + " probleme(s).");
            text.AppendLine();
            foreach (Step step in steps)
            {
                text.Append("- ").Append(step.Name).Append(" : ").Append(step.Outcome);
                if (!string.IsNullOrEmpty(step.Detail))
                {
                    text.Append(" (").Append(step.Detail).Append(')');
                }
                text.AppendLine();
            }
            File.WriteAllText(Path.Combine(output, "online.md"), text.ToString());
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
