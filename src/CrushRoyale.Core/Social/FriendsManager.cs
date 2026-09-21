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
    }
}
