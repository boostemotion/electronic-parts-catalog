package com.partshelf.catalog;

import android.content.Context;
import android.net.wifi.WifiManager;

import com.getcapacitor.JSArray;
import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;

import org.json.JSONException;
import org.json.JSONObject;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.Inet4Address;
import java.net.InetAddress;
import java.net.InterfaceAddress;
import java.net.NetworkInterface;
import java.net.SocketTimeoutException;
import java.nio.charset.StandardCharsets;
import java.util.Collections;
import java.util.Enumeration;
import java.util.LinkedHashSet;
import java.util.Set;

@CapacitorPlugin(name = "CatalogDiscovery")
public class CatalogDiscoveryPlugin extends Plugin {
    private static final String DISCOVERY_REQUEST = "COMPONENT_CATALOG_DISCOVER_V1";
    private static final int DISCOVERY_PORT = 10850;
    private static final int MAX_PACKET_BYTES = 1024;
    private static final int DEFAULT_TIMEOUT_MS = 1800;
    private static final int MIN_TIMEOUT_MS = 500;
    private static final int MAX_TIMEOUT_MS = 5000;

    @PluginMethod
    public void discover(PluginCall call) {
        Integer requestedTimeout = call.getInt("timeoutMs", DEFAULT_TIMEOUT_MS);
        int timeoutMs = Math.max(MIN_TIMEOUT_MS, Math.min(MAX_TIMEOUT_MS, requestedTimeout == null ? DEFAULT_TIMEOUT_MS : requestedTimeout));

        new Thread(() -> {
            JSObject result = new JSObject();
            result.put("responses", receiveResponses(timeoutMs));
            call.resolve(result);
        }, "catalog-discovery").start();
    }

    private JSArray receiveResponses(int timeoutMs) {
        JSArray responses = new JSArray();
        Set<String> received = new LinkedHashSet<>();
        WifiManager.MulticastLock multicastLock = acquireMulticastLock();

        try (DatagramSocket socket = new DatagramSocket()) {
            socket.setBroadcast(true);
            byte[] request = DISCOVERY_REQUEST.getBytes(StandardCharsets.UTF_8);
            for (InetAddress broadcastAddress : getBroadcastAddresses()) {
                socket.send(new DatagramPacket(request, request.length, broadcastAddress, DISCOVERY_PORT));
            }

            long deadline = System.currentTimeMillis() + timeoutMs;
            while (System.currentTimeMillis() < deadline) {
                socket.setSoTimeout((int) Math.max(1, deadline - System.currentTimeMillis()));
                DatagramPacket packet = new DatagramPacket(new byte[MAX_PACKET_BYTES], MAX_PACKET_BYTES);
                try {
                    socket.receive(packet);
                } catch (SocketTimeoutException ignored) {
                    break;
                }

                String payload = new String(packet.getData(), packet.getOffset(), packet.getLength(), StandardCharsets.UTF_8);
                if (!received.add(payload)) {
                    continue;
                }

                JSObject response = parseResponse(payload);
                if (response != null) {
                    responses.put(response);
                }
            }
        } catch (Exception ignored) {
            // Discovery is opportunistic: JS validates returned payloads and keeps the offline snapshot on failure.
        } finally {
            if (multicastLock != null && multicastLock.isHeld()) {
                multicastLock.release();
            }
        }

        return responses;
    }

    private Set<InetAddress> getBroadcastAddresses() throws Exception {
        Set<InetAddress> broadcasts = new LinkedHashSet<>();
        broadcasts.add(InetAddress.getByName("255.255.255.255"));

        Enumeration<NetworkInterface> interfaces = NetworkInterface.getNetworkInterfaces();
        for (NetworkInterface networkInterface : Collections.list(interfaces)) {
            if (!networkInterface.isUp() || networkInterface.isLoopback()) {
                continue;
            }

            for (InterfaceAddress interfaceAddress : networkInterface.getInterfaceAddresses()) {
                InetAddress address = interfaceAddress.getAddress();
                InetAddress broadcast = interfaceAddress.getBroadcast();
                if (address instanceof Inet4Address && broadcast instanceof Inet4Address) {
                    broadcasts.add(broadcast);
                }
            }
        }

        return broadcasts;
    }

    private JSObject parseResponse(String payload) {
        try {
            JSONObject response = new JSONObject(payload);
            JSObject result = new JSObject();
            result.put("service", response.optString("service", ""));
            result.put("version", response.optInt("version", 0));
            result.put("name", response.optString("name", ""));
            result.put("host", response.optString("host", ""));
            result.put("port", response.optInt("port", 0));
            result.put("path", response.optString("path", ""));
            result.put("readOnly", response.optBoolean("readOnly", false));
            return result;
        } catch (JSONException ignored) {
            return null;
        }
    }

    private WifiManager.MulticastLock acquireMulticastLock() {
        WifiManager wifiManager = (WifiManager) getContext().getApplicationContext().getSystemService(Context.WIFI_SERVICE);
        if (wifiManager == null) {
            return null;
        }

        WifiManager.MulticastLock multicastLock = wifiManager.createMulticastLock("catalog-discovery");
        multicastLock.setReferenceCounted(false);
        multicastLock.acquire();
        return multicastLock;
    }
}
