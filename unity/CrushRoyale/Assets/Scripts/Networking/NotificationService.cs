using System;
using CrushRoyale.Contracts;
using UnityEngine;
#if CRUSH_NOTIFICATIONS && UNITY_ANDROID
using Unity.Notifications.Android;
#endif

namespace CrushRoyale.Game.Networking
{
    /// <summary>
    /// Local notifications scheduled when the app goes to background: lives refilled, daily reward, weekly guild
    /// boss reset. Remote push (FCM) is optional and documented in docs/BACKEND.md.
    /// </summary>
    public sealed class NotificationService
    {
        private const string ChannelId = "crush_royale_general";

        private readonly Localization _loc;
        private readonly LocalSave _save;
        private bool _initialized;

        public NotificationService(Localization loc, LocalSave save)
        {
            _loc = loc;
            _save = save;
        }

        public void RequestPermission()
        {
#if CRUSH_NOTIFICATIONS && UNITY_ANDROID && !UNITY_EDITOR
            EnsureChannel();
            if (AndroidNotificationCenter.UserPermissionToPost != PermissionStatus.Allowed)
            {
                _ = new PermissionRequest();
            }
#endif
        }

        public void ScheduleReminders(ProfileDto profile)
        {
            if (!_save.Settings.NotificationsEnabled || profile == null)
            {
                return;
            }
#if CRUSH_NOTIFICATIONS && UNITY_ANDROID && !UNITY_EDITOR
            EnsureChannel();
            AndroidNotificationCenter.CancelAllScheduledNotifications();

            LivesDto lives = profile.Lives;
            if (lives != null && lives.Lives < lives.MaxRegen && lives.RechargeSeconds > 0)
            {
                double secondsToFull = lives.RechargeSeconds + (lives.MaxRegen - lives.Lives - 1) * 30 * 60;
                Send(_loc.T("notif.lives.title"), _loc.T("notif.lives.body"), DateTime.Now.AddSeconds(secondsToFull));
            }

            DateTime tomorrow = DateTime.Now.Date.AddDays(1).AddHours(19);
            Send(_loc.T("notif.daily.title"), _loc.T("notif.daily.body"), tomorrow);

            if (profile.GuildId.HasValue)
            {
                int daysToSaturday = ((int)DayOfWeek.Saturday - (int)DateTime.Now.DayOfWeek + 7) % 7;
                Send(_loc.T("notif.boss.title"), _loc.T("notif.boss.body"), DateTime.Now.Date.AddDays(daysToSaturday == 0 ? 7 : daysToSaturday).AddHours(11));
            }
#endif
        }

        public void CancelAll()
        {
#if CRUSH_NOTIFICATIONS && UNITY_ANDROID && !UNITY_EDITOR
            AndroidNotificationCenter.CancelAllScheduledNotifications();
            AndroidNotificationCenter.CancelAllDisplayedNotifications();
#endif
        }

#if CRUSH_NOTIFICATIONS && UNITY_ANDROID && !UNITY_EDITOR
        private void EnsureChannel()
        {
            if (_initialized)
            {
                return;
            }
            AndroidNotificationCenter.RegisterNotificationChannel(new AndroidNotificationChannel(ChannelId, "Crush Royale", _loc.T("notif.channel"), Importance.Default));
            _initialized = true;
        }

        private static void Send(string title, string body, DateTime fireTime)
        {
            var notification = new AndroidNotification(title, body, fireTime) { SmallIcon = "icon_0" };
            AndroidNotificationCenter.SendNotification(notification, ChannelId);
        }
#endif
    }
}
