using System;
using System.Threading.Tasks;
using CrushRoyale.Client;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Common;
using UnityEngine;

namespace CrushRoyale.Game.Networking
{
    /// <summary>
    /// The whole path of a free life: ask the player, play the video, wait for the ad network to tell the server
    /// about it, then collect.
    ///
    /// That middle step is the one worth knowing about. The reward is never granted on the strength of the game
    /// saying a video was watched, because the game runs on the player's phone and can be made to say anything.
    /// LevelPlay calls the server directly instead, signed, and only then is there anything to collect. That
    /// call usually lands a second or two after the video closes, sometimes a little later, so the claim is
    /// retried for a few seconds rather than failed on the first miss. If it never lands, nothing is granted and
    /// the player is told to try again, which is the right way round: a reward that failed to arrive is a
    /// nuisance, a reward granted on an unwatched video is a hole in the economy.
    /// </summary>
    public static class RewardedAdFlow
    {
        /// <summary>Roughly how long the network is given to call the server before giving up on the claim.</summary>
        private static readonly int[] RetryDelaysMs = { 400, 900, 1500, 2500, 4000 };

        /// <summary>True when there is a video to offer, so a button can hide itself rather than disappoint.</summary>
        public static bool CanOffer(GameRoot game) =>
            game != null && game.Backend.IsOnline && game.Ads != null && game.Ads.Provider.IsRewardedReady;

        /// <summary>
        /// Offers one life for a video. Returns true when the life was granted, in which case the wallet and the
        /// lives have already been applied to the local profile.
        /// </summary>
        public static async Task<bool> OfferFreeLifeAsync(GameRoot game)
        {
            if (!CanOffer(game))
            {
                return false;
            }
            bool watch = await game.UI.Dialog(
                game.Loc.T("ads.freeLifeTitle"),
                game.Loc.T("ads.freeLifeBody"),
                game.Loc.T("ads.watch"),
                game.Loc.T("common.cancel"));
            if (!watch)
            {
                return false;
            }
            return await WatchAndClaimAsync(game, "life");
        }

        private static async Task<bool> WatchAndClaimAsync(GameRoot game, string placement)
        {
            string token;
            try
            {
                token = await game.Ads.Provider.ShowRewardedAsync(placement);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Rewarded video failed: " + ex.Message);
                game.UI.Toast(game.Loc.T("ads.notReady"));
                return false;
            }
            if (string.IsNullOrEmpty(token))
            {
                // Closed early, or no ad was there after all. Nothing was watched, so nothing is owed.
                return false;
            }

            using (game.UI.Loading())
            {
                for (int attempt = 0; attempt < RetryDelaysMs.Length; attempt++)
                {
                    await Task.Delay(RetryDelaysMs[attempt]);
                    try
                    {
                        RewardedAdResponse granted = await game.Backend.Client.Api.RewardedAdAsync(placement, token);
                        game.Backend.ApplyWallet(granted.Wallet);
                        game.Backend.ApplyLives(granted.Lives);
                        game.Telemetry.Track("rewarded_ad", ("placement", placement), ("attempt", attempt + 1));
                        return true;
                    }
                    catch (CrushApiException ex) when (ex.Code == nameof(ErrorCode.AdNotVerified))
                    {
                        // The callback has not arrived yet. Keep waiting, a little longer each time.
                    }
                    catch (CrushApiException ex)
                    {
                        game.UI.ShowError(ex);
                        return false;
                    }
                }
            }
            game.UI.Toast(game.Loc.T("error.AdNotVerified"), 3f);
            return false;
        }
    }
}
