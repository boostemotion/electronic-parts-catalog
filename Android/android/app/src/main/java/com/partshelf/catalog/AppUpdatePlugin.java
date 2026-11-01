package com.partshelf.catalog;

import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.provider.Settings;

import androidx.core.content.FileProvider;

import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URI;
import java.security.MessageDigest;
import java.util.Locale;

@CapacitorPlugin(name = "AppUpdate")
public class AppUpdatePlugin extends Plugin {
    private static final String APK_PATH = "/mobile/app.apk";
    private static final String DOWNLOAD_DIRECTORY = "updates";
    private static final String DOWNLOAD_FILE_NAME = "app-update.apk";
    private static final int BUFFER_SIZE = 32 * 1024;

    @Override
    public void load() {
        deleteStaleDownload();
    }

    @PluginMethod
    public void downloadAndInstall(PluginCall call) {
        String apkUrl = call.getString("apkUrl");
        String sha256 = call.getString("sha256");
        Integer expectedSize = call.getInt("sizeBytes");
        if (!isValidApkUrl(apkUrl) || !isSha256(sha256) || expectedSize == null || expectedSize <= 0) {
            call.reject("Invalid Android update request.", "INVALID_UPDATE_REQUEST");
            return;
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && !getContext().getPackageManager().canRequestPackageInstalls()) {
            Intent settingsIntent = new Intent(
                Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                Uri.parse("package:" + getContext().getPackageName())
            );
            settingsIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            getContext().startActivity(settingsIntent);
            call.reject(
                "Unknown app sources permission is required. Enable it in Android Settings, then tap 更新 again.",
                "UNKNOWN_SOURCES_PERMISSION_REQUIRED"
            );
            return;
        }

        new Thread(() -> downloadVerifyAndLaunch(call, apkUrl, sha256.toLowerCase(Locale.ROOT), expectedSize), "app-update-download").start();
    }

    private void downloadVerifyAndLaunch(PluginCall call, String apkUrl, String expectedSha256, int expectedSize) {
        File apkFile = getDownloadFile();
        try {
            String actualSha256 = downloadApk(apkUrl, apkFile, expectedSize);
            if (!MessageDigest.isEqual(
                expectedSha256.getBytes(java.nio.charset.StandardCharsets.US_ASCII),
                actualSha256.getBytes(java.nio.charset.StandardCharsets.US_ASCII)
            )) {
                deleteFile(apkFile);
                call.reject("APK checksum verification failed", "APK_CHECKSUM_MISMATCH");
                return;
            }

            Uri apkUri = FileProvider.getUriForFile(
                getContext(),
                getContext().getPackageName() + ".fileprovider",
                apkFile
            );
            Intent installIntent = new Intent(Intent.ACTION_VIEW)
                .setDataAndType(apkUri, "application/vnd.android.package-archive")
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
            getContext().startActivity(installIntent);
            call.resolve();
        } catch (Exception exception) {
            deleteFile(apkFile);
            call.reject("Unable to prepare the Android update: " + exception.getMessage(), "APK_DOWNLOAD_FAILED", exception);
        }
    }

    private String downloadApk(String apkUrl, File destination, int expectedSize) throws Exception {
        File parent = destination.getParentFile();
        if (parent == null || (!parent.exists() && !parent.mkdirs())) {
            throw new IllegalStateException("Unable to create update cache directory.");
        }
        deleteFile(destination);

        HttpURLConnection connection = (HttpURLConnection) new URI(apkUrl).toURL().openConnection();
        connection.setInstanceFollowRedirects(false);
        connection.setConnectTimeout(10_000);
        connection.setReadTimeout(30_000);
        connection.setRequestMethod("GET");
        connection.setRequestProperty("Accept", "application/vnd.android.package-archive");
        if (connection.getResponseCode() != HttpURLConnection.HTTP_OK) {
            throw new IllegalStateException("Update download failed with HTTP " + connection.getResponseCode() + ".");
        }
        if (connection.getContentLengthLong() > expectedSize) {
            throw new IllegalStateException("APK is larger than the published release.");
        }

        MessageDigest digest = MessageDigest.getInstance("SHA-256");
        long total = 0;
        byte[] buffer = new byte[BUFFER_SIZE];
        try (InputStream input = connection.getInputStream(); FileOutputStream output = new FileOutputStream(destination)) {
            int count;
            while ((count = input.read(buffer)) != -1) {
                total += count;
                if (total > expectedSize) {
                    throw new IllegalStateException("APK is larger than the published release.");
                }
                digest.update(buffer, 0, count);
                output.write(buffer, 0, count);
            }
        } finally {
            connection.disconnect();
        }

        if (total != expectedSize) {
            throw new IllegalStateException("APK size does not match the published release.");
        }
        return toLowerHex(digest.digest());
    }

    private boolean isValidApkUrl(String value) {
        try {
            URI uri = new URI(value);
            return "http".equals(uri.getScheme()) &&
                uri.getUserInfo() == null &&
                uri.getRawQuery() == null &&
                uri.getRawFragment() == null &&
                APK_PATH.equals(uri.getPath()) &&
                isPrivateIpv4(uri.getHost()) &&
                uri.getPort() > 0 && uri.getPort() <= 65535;
        } catch (Exception ignored) {
            return false;
        }
    }

    private boolean isPrivateIpv4(String host) {
        if (host == null) {
            return false;
        }
        String[] octets = host.split("\\.", -1);
        if (octets.length != 4) {
            return false;
        }
        int[] values = new int[4];
        try {
            for (int index = 0; index < octets.length; index++) {
                if (octets[index].isEmpty() || (octets[index].length() > 1 && octets[index].startsWith("0"))) {
                    return false;
                }
                values[index] = Integer.parseInt(octets[index]);
                if (values[index] < 0 || values[index] > 255) {
                    return false;
                }
            }
        } catch (NumberFormatException ignored) {
            return false;
        }
        return values[0] == 10 ||
            (values[0] == 172 && values[1] >= 16 && values[1] <= 31) ||
            (values[0] == 192 && values[1] == 168);
    }

    private boolean isSha256(String value) {
        return value != null && value.matches("(?i)^[0-9a-f]{64}$");
    }

    private File getDownloadFile() {
        return new File(new File(getContext().getCacheDir(), DOWNLOAD_DIRECTORY), DOWNLOAD_FILE_NAME);
    }

    private void deleteStaleDownload() {
        deleteFile(getDownloadFile());
    }

    private void deleteFile(File file) {
        if (file.exists() && !file.delete()) {
            file.deleteOnExit();
        }
    }

    private String toLowerHex(byte[] bytes) {
        StringBuilder builder = new StringBuilder(bytes.length * 2);
        for (byte value : bytes) {
            builder.append(String.format(Locale.ROOT, "%02x", value));
        }
        return builder.toString();
    }
}
