package com.crushroyale.updater;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;

import java.io.File;
import java.io.FileNotFoundException;

/**
 * Read-only provider exposing the downloaded update APK to the system package installer (content:// URIs are required
 * since Android 7). Not exported: the installer only gets a temporary read grant for the one file.
 * Declared in the manifest by Assets/Editor/AndroidManifestHardening.cs (authority "&lt;applicationId&gt;.apkprovider").
 */
public final class ApkFileProvider extends ContentProvider {

    static final String FOLDER = "updates";
    static final String APK_MIME = "application/vnd.android.package-archive";

    static Uri uriFor(Context context, String fileName) {
        return Uri.parse("content://" + context.getPackageName() + ".apkprovider/" + Uri.encode(fileName));
    }

    private File fileFor(Uri uri) throws FileNotFoundException {
        String name = uri.getLastPathSegment();
        // Only plain APK names of the updates folder: no path traversal.
        if (name == null || name.contains("/") || name.contains("..") || !name.endsWith(".apk")) {
            throw new FileNotFoundException("Invalid update file");
        }
        File file = new File(new File(getContext().getCacheDir(), FOLDER), name);
        if (!file.isFile()) {
            throw new FileNotFoundException(name);
        }
        return file;
    }

    @Override
    public boolean onCreate() {
        return true;
    }

    @Override
    public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
        if (!"r".equals(mode)) {
            throw new FileNotFoundException("Read-only");
        }
        return ParcelFileDescriptor.open(fileFor(uri), ParcelFileDescriptor.MODE_READ_ONLY);
    }

    @Override
    public String getType(Uri uri) {
        return APK_MIME;
    }

    @Override
    public Cursor query(Uri uri, String[] projection, String selection, String[] selectionArgs, String sortOrder) {
        File file;
        try {
            file = fileFor(uri);
        } catch (FileNotFoundException e) {
            return null;
        }
        MatrixCursor cursor = new MatrixCursor(new String[] { OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE });
        cursor.addRow(new Object[] { file.getName(), file.length() });
        return cursor;
    }

    @Override
    public Uri insert(Uri uri, ContentValues values) {
        throw new UnsupportedOperationException();
    }

    @Override
    public int delete(Uri uri, String selection, String[] selectionArgs) {
        throw new UnsupportedOperationException();
    }

    @Override
    public int update(Uri uri, ContentValues values, String selection, String[] selectionArgs) {
        throw new UnsupportedOperationException();
    }
}
