using System;
using System.Collections.Generic;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;

namespace CrushRoyale.Core.Social
{
    public sealed class FriendsState
    {
        public List<string> Friends { get; set; } = new List<string>();

        public List<string> Incoming { get; set; } = new List<string>();

        public List<string> Outgoing { get; set; } = new List<string>();

        public List<string> Blocked { get; set; } = new List<string>();

        /// <summary>friendId -> last friendly challenge (unix ms).</summary>
        public Dictionary<string, long> LastChallengeUnixMs { get; set; } = new Dictionary<string, long>();

        /// <summary>UTC day of the last life sent to a friend (one gift per day).</summary>
        public int LifeSentDay { get; set; } = -1;

        /// <summary>UTC day of the last life accepted from a friend (one received per day).</summary>
        public int LifeTakenDay { get; set; } = -1;

        /// <summary>Ids of the friends whose life gift is waiting to be accepted.</summary>
        public List<string> LifeGifts { get; set; } = new List<string>();

        /// <summary>Duels in flight or freshly finished, newest first (see <see cref="FriendDuel"/>).</summary>
        public List<FriendDuel> Duels { get; set; } = new List<FriendDuel>();
    }

    /// <summary>Where a duel stands, from the point of view of the player holding the entry.</summary>
    public enum DuelState : byte
    {
        /// <summary>The challenger has not played their run yet.</summary>
        ChallengerPlaying = 0,

        /// <summary>The invitation is waiting for this player: play the same board or decline.</summary>
        Invited = 1,

        /// <summary>This player has played; waiting for the other one.</summary>
        WaitingOpponent = 2,

        /// <summary>Both scores are in (or the duel was declined): <see cref="FriendDuel.Outcome"/> tells what happened.</summary>
        Finished = 3
    }

    public enum DuelOutcome : byte
    {
        None = 0,
        Won = 1,
        Lost = 2,
        Draw = 3,
        Declined = 4
    }

    /// <summary>
    /// A real duel between two friends: both play the SAME board and the scores are compared. Each player keeps their
    /// own copy of the entry, so a duel never needs a table of its own.
    /// </summary>
    public sealed class FriendDuel
    {
        public string Id { get; set; }

        public string OpponentId { get; set; }

        public string OpponentName { get; set; }

        public ulong Seed { get; set; }

        public bool IamChallenger { get; set; }

        public DuelState State { get; set; }

        public DuelOutcome Outcome { get; set; }

        /// <summary>Score to beat (0 until the challenger has played).</summary>
        public long OpponentScore { get; set; }

        public long MyScore { get; set; }

        /// <summary>Replay of the opponent's run, replayed as a live ghost when this player takes the duel.</summary>
        public long OpponentReplayId { get; set; }

        public long CreatedUnixMs { get; set; }

        public long UpdatedUnixMs { get; set; }
    }

    /// <summary>
    /// Friends (GDD "Friends System"): requests by id, friendly challenges without trophy risk, 5-minute challenge
    /// cooldown per friend. Both players' states are passed in so the server can update them in one transaction.
    /// </summary>
    public sealed class FriendsManager
    {
        /// <summary>A life offered by a friend, and whether it can be accepted right now.</summary>
        public sealed class LifeGiftView
        {
            public string FriendId { get; internal set; }

            public bool CanAccept { get; internal set; }
        }

        private readonly GameBalance _balance;
        private readonly IClock _clock;

        public FriendsManager(GameBalance balance, IClock clock)
        {
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public OperationResult SendRequest(string myId, FriendsState mine, string theirId, FriendsState theirs)
        {
            Require(mine, theirs);
            if (string.IsNullOrEmpty(myId) || string.IsNullOrEmpty(theirId) || myId == theirId)
            {
                return OperationResult.Fail(ErrorCode.InvalidArgument);
            }
            if (mine.Friends.Contains(theirId))
            {
                return OperationResult.Fail(ErrorCode.AlreadyMember);
            }
            if (mine.Blocked.Contains(theirId) || theirs.Blocked.Contains(myId))
            {
                return OperationResult.Fail(ErrorCode.PermissionDenied);
            }
            if (mine.Incoming.Contains(theirId))
            {
                return Accept(myId, mine, theirId, theirs);
            }
            if (mine.Outgoing.Contains(theirId))
            {
                return OperationResult.Fail(ErrorCode.DuplicateRequest);
            }
            if (mine.Friends.Count >= _balance.Social.MaxFriends || theirs.Friends.Count >= _balance.Social.MaxFriends)
            {
                return OperationResult.Fail(ErrorCode.LimitReached, "Friend list full.");
            }
            if (theirs.Incoming.Count >= _balance.Social.PendingRequestsMax || mine.Outgoing.Count >= _balance.Social.PendingRequestsMax)
            {
                return OperationResult.Fail(ErrorCode.LimitReached, "Too many pending requests.");
            }

            mine.Outgoing.Add(theirId);
            theirs.Incoming.Add(myId);
            return OperationResult.Ok();
        }

        public OperationResult Accept(string myId, FriendsState mine, string theirId, FriendsState theirs)
        {
            Require(mine, theirs);
            if (!mine.Incoming.Contains(theirId))
            {
                return OperationResult.Fail(ErrorCode.NotFound);
            }
            if (mine.Friends.Count >= _balance.Social.MaxFriends || theirs.Friends.Count >= _balance.Social.MaxFriends)
            {
                return OperationResult.Fail(ErrorCode.LimitReached);
            }

            mine.Incoming.Remove(theirId);
            theirs.Outgoing.Remove(myId);
            if (!mine.Friends.Contains(theirId))
            {
                mine.Friends.Add(theirId);
            }
            if (!theirs.Friends.Contains(myId))
            {
                theirs.Friends.Add(myId);
            }
            return OperationResult.Ok();
        }

        public OperationResult Decline(string myId, FriendsState mine, string theirId, FriendsState theirs)
        {
            Require(mine, theirs);
            if (!mine.Incoming.Remove(theirId))
            {
                return OperationResult.Fail(ErrorCode.NotFound);
            }
            theirs.Outgoing.Remove(myId);
            return OperationResult.Ok();
        }

        public OperationResult Remove(string myId, FriendsState mine, string theirId, FriendsState theirs)
        {
            Require(mine, theirs);
            if (!mine.Friends.Remove(theirId))
            {
                return OperationResult.Fail(ErrorCode.NotFound);
            }
            theirs.Friends.Remove(myId);
            mine.LastChallengeUnixMs.Remove(theirId);
            theirs.LastChallengeUnixMs.Remove(myId);
            return OperationResult.Ok();
        }

        public OperationResult Block(string myId, FriendsState mine, string theirId, FriendsState theirs)
        {
            Require(mine, theirs);
            if (myId == theirId)
            {
                return OperationResult.Fail(ErrorCode.InvalidArgument);
            }
            mine.Friends.Remove(theirId);
            theirs.Friends.Remove(myId);
            mine.Incoming.Remove(theirId);
            mine.Outgoing.Remove(theirId);
            theirs.Incoming.Remove(myId);
            theirs.Outgoing.Remove(myId);
            if (!mine.Blocked.Contains(theirId))
            {
                mine.Blocked.Add(theirId);
            }
            return OperationResult.Ok();
        }

        public long ChallengeCooldownRemainingMs(FriendsState mine, string friendId)
        {
            if (mine == null || friendId == null || !mine.LastChallengeUnixMs.TryGetValue(friendId, out long last))
            {
                return 0;
            }
            return Math.Max(0, last + _balance.Social.ChallengeCooldownMs - TimeUtil.ToUnixMs(_clock.UtcNow));
        }

        public ErrorCode CanChallenge(FriendsState mine, string friendId)
        {
            if (mine == null || friendId == null || !mine.Friends.Contains(friendId))
            {
                return ErrorCode.NotFound;
            }
            return ChallengeCooldownRemainingMs(mine, friendId) > 0 ? ErrorCode.CooldownActive : ErrorCode.None;
        }

        public OperationResult RecordChallenge(FriendsState mine, string friendId)
        {
            ErrorCode check = CanChallenge(mine, friendId);
            if (check != ErrorCode.None)
            {
                return OperationResult.Fail(check);
            }
            mine.LastChallengeUnixMs[friendId] = TimeUtil.ToUnixMs(_clock.UtcNow);
            return OperationResult.Ok();
        }

        /// <summary>VIP 9-10 friendly challenge perk: deterministic roll (seeded by the match) to copy one of the friend's power-ups.</summary>
        public static bool RollPowerUpSteal(GameBalance balance, VipTier tier, GameMode mode, ulong matchSeed)
        {
            var vip = new VipSystem(balance);
            if (!vip.CanStealPowerUp(tier, mode))
            {
                return false;
            }
            return DeterministicRandom.Derive(matchSeed, 0x5745414CUL).ChancePermille(vip.GetBenefit(tier).StealChancePermille);
        }

        private static void Require(FriendsState mine, FriendsState theirs)
        {
            if (mine == null)
            {
                throw new ArgumentNullException(nameof(mine));
            }
            if (theirs == null)
            {
                throw new ArgumentNullException(nameof(theirs));
            }
        }

        // ------------------------------------------------------------------ life gifts

        /// <summary>One life can be offered to one friend per UTC day; the friend accepts it when a slot is free.</summary>
        public OperationResult SendLife(string senderId, FriendsState mine, string friendId, FriendsState theirs, int today)
        {
            if (mine == null || theirs == null || string.IsNullOrEmpty(friendId))
            {
                return OperationResult.Fail(ErrorCode.InvalidArgument);
            }
            if (!mine.Friends.Contains(friendId))
            {
                return OperationResult.Fail(ErrorCode.NotFound);
            }
            if (mine.LifeSentDay == today)
            {
                return OperationResult.Fail(ErrorCode.LimitReached, "A life was already sent today.");
            }
            if (theirs.LifeGifts.Contains(senderId))
            {
                return OperationResult.Fail(ErrorCode.LimitReached, "This friend already has a life waiting.");
            }
            mine.LifeSentDay = today;
            theirs.LifeGifts.Add(senderId);
            return OperationResult.Ok();
        }

        /// <summary>Accepts one waiting life. Refused when the day is used up or the player is already full.</summary>
        public OperationResult AcceptLife(FriendsState mine, int today, int currentLives, int maxLives)
        {
            if (mine == null || mine.LifeGifts.Count == 0)
            {
                return OperationResult.Fail(ErrorCode.NotFound);
            }
            if (mine.LifeTakenDay == today)
            {
                return OperationResult.Fail(ErrorCode.LimitReached, "A life was already received today.");
            }
            if (currentLives >= maxLives)
            {
                // Refusing here is the point: a full player must not waste a friend gift.
                return OperationResult.Fail(ErrorCode.LimitReached, "Lives are already full.");
            }
            mine.LifeTakenDay = today;
            mine.LifeGifts.RemoveAt(0);
            return OperationResult.Ok();
        }

        /// <summary>True when a life can still be offered today.</summary>
        public static bool CanSendLife(FriendsState state, int today) => state != null && state.LifeSentDay != today;

        /// <summary>True when a waiting life can be accepted right now.</summary>
        public static bool CanAcceptLife(FriendsState state, int today, int currentLives, int maxLives) =>
            state != null && state.LifeGifts.Count > 0 && state.LifeTakenDay != today && currentLives < maxLives;

        // ------------------------------------------------------------------ duels

        /// <summary>Duels kept per player: older finished ones are dropped so the list stays readable.</summary>
        public const int MaxDuels = 12;

        /// <summary>Opens a duel: both sides get their entry, the challenger plays first.</summary>
        public OperationResult StartDuel(FriendsState mine, string myName, FriendsState theirs, string theirName,
            string myId, string theirId, string duelId, ulong seed, long nowMs)
        {
            if (mine == null || theirs == null || string.IsNullOrEmpty(duelId))
            {
                return OperationResult.Fail(ErrorCode.InvalidArgument);
            }
            if (!mine.Friends.Contains(theirId))
            {
                return OperationResult.Fail(ErrorCode.NotFound);
            }
            if (mine.Duels.Exists(d => d.OpponentId == theirId && d.State != DuelState.Finished))
            {
                return OperationResult.Fail(ErrorCode.DuplicateRequest, "A duel with this friend is already running.");
            }
            Add(mine, new FriendDuel
            {
                Id = duelId, OpponentId = theirId, OpponentName = theirName, Seed = seed, IamChallenger = true,
                State = DuelState.ChallengerPlaying, CreatedUnixMs = nowMs, UpdatedUnixMs = nowMs
            });
            Add(theirs, new FriendDuel
            {
                Id = duelId, OpponentId = myId, OpponentName = myName, Seed = seed, IamChallenger = false,
                State = DuelState.ChallengerPlaying, CreatedUnixMs = nowMs, UpdatedUnixMs = nowMs
            });
            return OperationResult.Ok();
        }

        /// <summary>Records a run. Returns the outcome for the player who just played (None while the duel is open).</summary>
        public DuelOutcome RecordDuelRun(FriendsState mine, FriendsState theirs, string duelId, long score, long replayId, long nowMs)
        {
            FriendDuel mineEntry = Find(mine, duelId);
            FriendDuel theirEntry = Find(theirs, duelId);
            if (mineEntry == null)
            {
                return DuelOutcome.None;
            }
            mineEntry.MyScore = score;
            mineEntry.UpdatedUnixMs = nowMs;
            if (theirEntry != null)
            {
                theirEntry.OpponentScore = score;
                theirEntry.OpponentReplayId = replayId;
                theirEntry.UpdatedUnixMs = nowMs;
            }

            bool bothPlayed = theirEntry != null && theirEntry.MyScore > 0;
            if (!bothPlayed)
            {
                mineEntry.State = DuelState.WaitingOpponent;
                if (theirEntry != null)
                {
                    theirEntry.State = DuelState.Invited;
                }
                return DuelOutcome.None;
            }

            DuelOutcome mineOutcome = score > theirEntry.MyScore ? DuelOutcome.Won : score < theirEntry.MyScore ? DuelOutcome.Lost : DuelOutcome.Draw;
            mineEntry.State = DuelState.Finished;
            mineEntry.Outcome = mineOutcome;
            mineEntry.OpponentScore = theirEntry.MyScore;
            theirEntry.State = DuelState.Finished;
            theirEntry.Outcome = mineOutcome == DuelOutcome.Won ? DuelOutcome.Lost : mineOutcome == DuelOutcome.Lost ? DuelOutcome.Won : DuelOutcome.Draw;
            return mineOutcome;
        }

        /// <summary>Turns down an invitation: the challenger is told instead of waiting forever.</summary>
        public OperationResult DeclineDuel(FriendsState mine, FriendsState theirs, string duelId, long nowMs)
        {
            FriendDuel mineEntry = Find(mine, duelId);
            if (mineEntry == null || mineEntry.State == DuelState.Finished)
            {
                return OperationResult.Fail(ErrorCode.NotFound);
            }
            mineEntry.State = DuelState.Finished;
            mineEntry.Outcome = DuelOutcome.Declined;
            mineEntry.UpdatedUnixMs = nowMs;
            FriendDuel theirEntry = Find(theirs, duelId);
            if (theirEntry != null)
            {
                theirEntry.State = DuelState.Finished;
                theirEntry.Outcome = DuelOutcome.Declined;
                theirEntry.UpdatedUnixMs = nowMs;
            }
            return OperationResult.Ok();
        }

        /// <summary>Removes a finished duel the player has seen.</summary>
        public bool DismissDuel(FriendsState mine, string duelId)
        {
            FriendDuel entry = Find(mine, duelId);
            return entry != null && entry.State == DuelState.Finished && mine.Duels.Remove(entry);
        }

        public static FriendDuel Find(FriendsState state, string duelId) =>
            state?.Duels.Find(d => d.Id == duelId);

        private static void Add(FriendsState state, FriendDuel duel)
        {
            state.Duels.Insert(0, duel);
            while (state.Duels.Count > MaxDuels)
            {
                int last = state.Duels.FindLastIndex(d => d.State == DuelState.Finished);
                if (last < 0)
                {
                    break;
                }
                state.Duels.RemoveAt(last);
            }
        }
    }
}
