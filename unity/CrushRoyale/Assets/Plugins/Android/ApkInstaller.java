package com.crushroyale.updater;

import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageInfo;
import android.net.Uri;
import android.os.Build;
import android.provider.Settings;

import java.io.File;

/**
 * In-game updater for the directly-installed APK (not used for Google Play installs, which update through the store).
 * The APK is downloaded by Unity into {@link #updatesDir(Activity)} and handed to the system installer through
 * {@link ApkFileProvider}; Android always asks the player to confirm the update (one tap).
 */
public final class ApkInstaller {

    private ApkInstaller() {
    }

    public static long versionCode(Activity activity) {
        try {
            PackageInfo info = activity.getPackageManager().getPackageInfo(activity.getPackageName(), 0);
            return Build.VERSION.SDK_INT >= 28 ? info.getLongVersionCode() : info.versionCode;
        } catch (Exception e) {
            return -1;
        }
    }

    public static String updatesDir(Activity activity) {
        File dir = new File(activity.getCacheDir(), ApkFileProvider.FOLDER);
        dir.mkdirs();
        return dir.getAbsolutePath();
    }

    /** Android 8+: the "install unknown apps" permission is granted per app, once, in the system settings. */
    public static boolean canInstall(Activity activity) {
        return Build.VERSION.SDK_INT < 26 || activity.getPackageManager().canRequestPackageInstalls();
    }

    public static void openInstallPermission(Activity activity) {
        if (Build.VERSION.SDK_INT < 26) {
            return;
        }
        Intent intent = new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + activity.getPackageName()));
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        activity.startActivity(intent);
    }

    /** Opens the system "update this app?" screen for a file of the updates folder. */
    public static boolean install(Activity activity, String fileName) {
        try {
            Uri uri = ApkFileProvider.uriFor(activity, fileName);
            Intent intent = new Intent(Intent.ACTION_VIEW);
            intent.setDataAndType(uri, ApkFileProvider.APK_MIME);
            intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
            activity.startActivity(intent);
            return true;
        } catch (Exception e) {
            return false;
        }
    }
}
