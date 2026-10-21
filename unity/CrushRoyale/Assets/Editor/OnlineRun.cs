using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CrushRoyale.Client;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Gameplay;
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

            // Le compte releve est charge dans le serveur local quand il y en a un, et c'est lui qui joue.
            // Sinon un joueur neuf, different a chaque passage, pour qu'un essai ne depende jamais du precedent.
            string player = ImportAccount(server) ?? Guid.NewGuid().ToString();
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

                    foreach (string piece in (Argument("-stages") ?? string.Empty).Split(','))
                    {
                        if (int.TryParse(piece.Trim(), out int stageId))
                        {
                            problems += await PlayStageAsync(game, stageId, steps);
                        }
                    }
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

        /// <summary>
        /// Joue une etape du debut a la fin comme le telephone le ferait, et laisse le serveur decider.
        ///
        /// C'est la seule maniere de verifier la chaine entiere : le serveur distribue le plateau, le moteur
        /// joue, la rejouee repart, le serveur la rejoue coup par coup pour verifier le score, accorde les
        /// etoiles et les recompenses, et enregistre la progression. Un score calcule ici et jamais envoye ne
        /// prouve rien de tout cela.
        /// </summary>
        /// <summary>
        /// Depose le compte releve dans le serveur local et renvoie son identifiant, ou null s'il n'y en a pas.
        ///
        /// La route n'existe qu'en developpement : un serveur de production ne l'expose pas du tout.
        /// </summary>
        private static string ImportAccount(string server)
        {
            const string path = "Assets/Editor/Fixtures/playerRecord.json";
            if (!File.Exists(path))
            {
                Debug.Log("OnlineRun: pas de compte releve, un joueur neuf sera cree.");
                return null;
            }
            try
            {
                string body = File.ReadAllText(path);
                using var http = new System.Net.Http.HttpClient();
                var content = new System.Net.Http.StringContent(body, Encoding.UTF8, "application/json");
                System.Net.Http.HttpResponseMessage response =
                    http.PostAsync(server.TrimEnd('/') + "/dev/import-player", content).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                string id = Newtonsoft.Json.Linq.JObject.Parse(body)["id"]?.ToString();
                Debug.Log("OnlineRun: compte " + id + " charge dans le serveur local.");
                return id;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("OnlineRun: import impossible (" + ex.Message + "), un joueur neuf sera cree.");
                return null;
            }
        }

        private static async Task<int> PlayStageAsync(GameRoot game, int stageId, List<Step> steps)
        {
            return await Try(steps, "etape " + stageId, async () =>
            {
                // Un chronometre ordinaire, et non l'horloge d'Unity : celle-ci ne se lit que depuis le fil
                // principal, et tout ceci se passe ailleurs.
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                MatchStartResponse start = await game.Backend.Client.Api.StartStageAsync(stageId, new StartStageRequest());
                SessionConfig config = MatchConfigFactory.Build(start, game.Backend.Balance, game.Backend.Catalog);
                var session = new GameSession(config, game.Backend.Balance, game.Backend.PlayerId);
                StageResult local = HeadlessRunner.Run(session, new GreedyBot());

                // Le bot joue une etape de quatre-vingt-dix secondes en deux millisecondes. Le serveur compare
                // la duree annoncee par la rejouee au temps reellement ecoule depuis qu'il a distribue le
                // plateau, et refuse l'ecart : c'est exactement la triche qu'il doit arreter. Il faut donc
                // attendre, pour de vrai, le temps que la partie a dure.
                long owed = session.Replay.EndTimeMs - elapsed.ElapsedMilliseconds;
                if (owed > 0)
                {
                    Debug.Log("OnlineRun: la partie a dure " + session.Replay.EndTimeMs
                              + " ms, attente de " + owed + " ms avant l'envoi.");
                    await Task.Delay((int)Math.Min(owed + 500, 300000));
                }

                StageCompleteResponse server = await game.Backend.Client.SubmitStageAsync(start.MatchId, session.Replay);
                if (server == null)
                {
                    throw new Exception("aucune reponse du serveur (mise en file d'attente)");
                }
                if (!server.Accepted)
                {
                    throw new Exception("refusee par le serveur : " + server.Error);
                }
                // Relu depuis le serveur, et non depuis ce qui vient d'etre joue : c'est ce qui prouve que la
                // partie a ete enregistree et pas seulement acceptee.
                ProfileDto after = await game.Backend.Client.Api.GetMeAsync();
                return server.State + ", score " + server.Score + " (local " + local.FinalScore + "), "
                       + server.Stars + " etoile(s), +" + server.CoinsEarned + " pieces, +"
                       + server.OrbesEarned + " orbes, etape atteinte " + after.Story?.HighestUnlockedStage;
            });
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
