package com.getcapacitor.myapp;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertTrue;

import org.junit.Test;
import org.w3c.dom.Document;
import org.w3c.dom.Element;
import org.w3c.dom.Node;
import org.w3c.dom.NodeList;

import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;

import javax.xml.parsers.DocumentBuilderFactory;

public class ExampleUnitTest {
    private static final String ANDROID_NAMESPACE = "http://schemas.android.com/apk/res/android";

    @Test
    public void mainActivityRegistersCustomPluginsBeforeBridgeInitialization() throws Exception {
        String activity = readAppSource("src/main/java/com/partshelf/catalog/MainActivity.java");

        int appUpdateRegistration = activity.indexOf("registerPlugin(AppUpdatePlugin.class)");
        int discoveryRegistration = activity.indexOf("registerPlugin(CatalogDiscoveryPlugin.class)");
        int bridgeInitialization = activity.indexOf("super.onCreate(savedInstanceState)");

        assertTrue(appUpdateRegistration >= 0);
        assertTrue(discoveryRegistration >= 0);
        assertTrue(bridgeInitialization >= 0);
        assertTrue(appUpdateRegistration < bridgeInitialization);
        assertTrue(discoveryRegistration < bridgeInitialization);
    }

    @Test
    public void manifestDisablesBackupsAndUsesTheNetworkSecurityConfiguration() throws Exception {
        Document manifest = parseXml(readAppSource("src/main/AndroidManifest.xml"));
        Element application = (Element) manifest.getElementsByTagName("application").item(0);

        assertNotNull(application);
        assertEquals("false", application.getAttributeNS(ANDROID_NAMESPACE, "allowBackup"));
        assertEquals("@xml/network_security_config", application.getAttributeNS(ANDROID_NAMESPACE, "networkSecurityConfig"));
    }

    @Test
    public void fileProviderExposesOnlyTheUpdateCacheDirectory() throws Exception {
        Document manifest = parseXml(readAppSource("src/main/AndroidManifest.xml"));
        NodeList providers = manifest.getElementsByTagName("provider");
        Element fileProvider = null;
        for (int index = 0; index < providers.getLength(); index++) {
            Element provider = (Element) providers.item(index);
            if ("androidx.core.content.FileProvider".equals(provider.getAttributeNS(ANDROID_NAMESPACE, "name"))) {
                fileProvider = provider;
                break;
            }
        }

        assertNotNull(fileProvider);
        assertEquals("false", fileProvider.getAttributeNS(ANDROID_NAMESPACE, "exported"));
        assertEquals("true", fileProvider.getAttributeNS(ANDROID_NAMESPACE, "grantUriPermissions"));
        Node metaData = fileProvider.getElementsByTagName("meta-data").item(0);
        assertNotNull(metaData);
        assertEquals("@xml/file_paths", ((Element) metaData).getAttributeNS(ANDROID_NAMESPACE, "resource"));

        String filePaths = readAppSource("src/main/res/xml/file_paths.xml");
        assertTrue(filePaths.contains("<cache-path name=\"updates\" path=\"updates/\" />"));
        assertFalse(filePaths.contains("external-path"));
        assertFalse(filePaths.contains("files-path"));
        assertFalse(filePaths.contains("root-path"));
    }

    @Test
    public void appUpdatePluginRestrictsUpdateRequestsAndVerifiesDownloads() throws Exception {
        String plugin = readAppSource("src/main/java/com/partshelf/catalog/AppUpdatePlugin.java");

        assertTrue(plugin.contains("@CapacitorPlugin(name = \"AppUpdate\")"));
        assertTrue(plugin.contains("private static final String APK_PATH = \"/mobile/app.apk\""));
        assertTrue(plugin.contains("\"http\".equals(uri.getScheme())"));
        assertTrue(plugin.contains("uri.getUserInfo() == null"));
        assertTrue(plugin.contains("uri.getRawQuery() == null"));
        assertTrue(plugin.contains("uri.getRawFragment() == null"));
        assertTrue(plugin.contains("isPrivateIpv4(uri.getHost())"));
        assertTrue(plugin.contains("connection.setInstanceFollowRedirects(false)"));
        assertTrue(plugin.contains("connection.setConnectTimeout(10_000)"));
        assertTrue(plugin.contains("connection.setReadTimeout(30_000)"));
        assertTrue(plugin.contains("MessageDigest.isEqual("));
        assertTrue(plugin.contains("getContext().getCacheDir()"));
        assertTrue(plugin.contains("FileProvider.getUriForFile("));
    }

    private static String readAppSource(String relativePath) throws IOException {
        Path appDirectory = Paths.get(System.getProperty("catalog.appSourceDir", System.getProperty("user.dir")));
        return new String(Files.readAllBytes(appDirectory.resolve(relativePath)), StandardCharsets.UTF_8);
    }

    private static Document parseXml(String source) throws Exception {
        DocumentBuilderFactory factory = DocumentBuilderFactory.newInstance();
        factory.setNamespaceAware(true);
        return factory.newDocumentBuilder().parse(new ByteArrayInputStream(source.getBytes(StandardCharsets.UTF_8)));
    }
}
