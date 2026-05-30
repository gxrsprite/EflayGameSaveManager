package com.eflay.gamesavemanager.service

import android.util.Log
import com.eflay.gamesavemanager.model.*
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import java.io.File
import java.io.FileInputStream
import java.io.InputStream
import java.net.HttpURLConnection
import java.net.URLEncoder
import java.security.MessageDigest
import java.text.SimpleDateFormat
import java.util.*
import java.util.concurrent.TimeUnit
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec
import javax.xml.parsers.DocumentBuilderFactory


class S3CloudService {

    private val client = OkHttpClient.Builder()
        .connectTimeout(30, TimeUnit.SECONDS)
        .readTimeout(30, TimeUnit.SECONDS)
        .writeTimeout(30, TimeUnit.SECONDS)
        .retryOnConnectionFailure(true)
        .build()

    private val json = Json { ignoreUnknownKeys = true; prettyPrint = false }

    companion object {
        private const val TAG = "S3CloudService"
        private const val EMPTY_PAYLOAD_HASH = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    }

    suspend fun getGameStatus(
        game: GameSnapshot,
        currentDevice: CurrentDeviceContext,
        cloudSettings: CloudSettings
    ): GameCloudStatus = withContext(Dispatchers.IO) {
        Log.d(TAG, "getGameStatus: game=${game.name}, device=${currentDevice.deviceId}")
        val backups = tryLoadGameBackups(game.name, cloudSettings)
        Log.d(TAG, "getGameStatus: backups loaded=${backups != null}, count=${backups?.Backups?.size ?: 0}")
        val currentHead = backups?.let { resolveCurrentHead(it, currentDevice.deviceId) }
        GameCloudStatus(
            gameName = game.name,
            isAvailable = backups != null && backups.Backups.isNotEmpty(),
            rootKey = getGameRootKey(cloudSettings, game.name),
            currentHead = currentHead?.let { parseBackupTimestamp(it) },
            backupCount = backups?.Backups?.size ?: 0,
            currentHeadDate = currentHead
        )
    }

    suspend fun listGameBackups(
        game: GameSnapshot,
        currentDevice: CurrentDeviceContext,
        cloudSettings: CloudSettings
    ): List<CloudGameBackup> = withContext(Dispatchers.IO) {
        val backups = tryLoadGameBackups(game.name, cloudSettings) ?: return@withContext emptyList()
        val currentHead = resolveCurrentHead(backups, currentDevice.deviceId)
        backups.Backups
            .sortedByDescending { it.Date }
            .map { entry ->
                CloudGameBackup(
                    gameName = game.name,
                    date = entry.Date,
                    describe = entry.Describe,
                    path = entry.Path,
                    size = entry.Size,
                    parent = entry.Parent,
                    deviceId = entry.DeviceId,
                    isCurrentDeviceHead = entry.Date == currentHead,
                    isAnyDeviceHead = backups.DeviceHeads[entry.DeviceId]?.let { it == entry.Date } == true
                )
            }
    }

    suspend fun uploadCurrentSave(
        game: GameSnapshot,
        currentDevice: CurrentDeviceContext,
        cloudSettings: CloudSettings,
        archivePath: String
    ): CloudUploadResult = withContext(Dispatchers.IO) {
        val rootKey = getGameRootKey(cloudSettings, game.name)
        val archiveFile = File(archivePath)
        val timestamp = SimpleDateFormat("yyyy-MM-dd_HH-mm-ss", Locale.US).format(Date())
        val archiveKey = "$rootKey/$timestamp.zip"

        uploadFile(cloudSettings.backend, archiveKey, archiveFile)

        // Update Backups.json manifest
        val existingBackups = tryLoadGameBackups(game.name, cloudSettings)
        val saveDataRootKey = getSaveDataRootKey(cloudSettings)
        val newEntry = LegacyBackupEntry(
            Date = timestamp,
            Describe = "",
            Path = ".\\save_data\\${game.name}\\$timestamp.zip",
            Size = archiveFile.length(),
            Parent = null,
            DeviceId = currentDevice.deviceId
        )

        val updatedBackups = if (existingBackups != null) {
            val filteredBackups = existingBackups.Backups.filter {
                !(it.Date == timestamp && it.DeviceId == currentDevice.deviceId)
            } + newEntry
            val newHeads = existingBackups.DeviceHeads.toMutableMap()
            newHeads[currentDevice.deviceId] = timestamp
            existingBackups.copy(Backups = filteredBackups, DeviceHeads = newHeads)
        } else {
            LegacyGameBackups(
                Name = game.name,
                Backups = listOf(newEntry),
                DeviceHeads = mapOf(currentDevice.deviceId to timestamp)
            )
        }

        val manifestJson = json.encodeToString(LegacyGameBackups.serializer(), updatedBackups)
        uploadUtf8Json(cloudSettings.backend, "$rootKey/Backups.json", manifestJson)

        updateSyncState(cloudSettings, currentDevice, game.name, timestamp)

        CloudUploadResult(
            rootKey = rootKey,
            uploadedObjectCount = 2,
            uploadedByteCount = archiveFile.length() + manifestJson.toByteArray().size.toLong()
        )
    }

    suspend fun restoreCurrentSave(
        game: GameSnapshot,
        currentDevice: CurrentDeviceContext,
        cloudSettings: CloudSettings,
        extractTargetDir: String
    ): CloudDownloadResult = withContext(Dispatchers.IO) {
        val backups = tryLoadGameBackups(game.name, cloudSettings)
            ?: throw IllegalStateException("No cloud backups found for '${game.name}'.")
        val currentBackup = resolveCurrentBackup(backups, currentDevice.deviceId)
            ?: throw IllegalStateException("No cloud backups found for '${game.name}'.")

        val archiveKey = resolveArchiveKey(currentBackup, cloudSettings, game.name)
        val tempDir = File(extractTargetDir, "cloud-restore-${UUID.randomUUID()}")
        tempDir.mkdirs()
        val archivePath = File(tempDir, "cloud-save.zip")

        try {
            downloadFile(cloudSettings.backend, archiveKey, archivePath)
            updateSyncState(cloudSettings, currentDevice, game.name, currentBackup.Date)

            CloudDownloadResult(
                rootKey = archiveKey,
                downloadedObjectCount = 1,
                downloadedByteCount = archivePath.length(),
                archivePath = archivePath.absolutePath
            )
        } finally {
            // tempDir will be cleaned up by caller
        }
    }

    suspend fun downloadBackupArchive(
        game: GameSnapshot,
        cloudSettings: CloudSettings,
        backupDate: String,
        deviceId: String?,
        destinationFile: File
    ): CloudDownloadResult = withContext(Dispatchers.IO) {
        val backups = tryLoadGameBackups(game.name, cloudSettings)
            ?: throw IllegalStateException("No cloud backups found for '${game.name}'.")
        val backup = findBackup(backups, backupDate, deviceId)
            ?: throw IllegalStateException("Cloud backup not found: $backupDate")
        val archiveKey = resolveArchiveKey(backup, cloudSettings, game.name)

        downloadFile(cloudSettings.backend, archiveKey, destinationFile)

        CloudDownloadResult(
            rootKey = archiveKey,
            downloadedObjectCount = 1,
            downloadedByteCount = destinationFile.length()
        )
    }

    // --- Private helpers ---

    private suspend fun tryLoadGameBackups(
        gameName: String,
        cloudSettings: CloudSettings
    ): LegacyGameBackups? = withContext(Dispatchers.IO) {
        val rootKey = getGameRootKey(cloudSettings, gameName)
        val objectKey = "$rootKey/Backups.json"
        Log.d(TAG, "tryLoadGameBackups: fetching $objectKey")
        val jsonStr = tryDownloadUtf8String(cloudSettings.backend, objectKey)
        Log.d(TAG, "tryLoadGameBackups: response jsonStr=${jsonStr != null}, len=${jsonStr?.length ?: 0}")
        jsonStr?.let {
            Log.d(TAG, "tryLoadGameBackups: raw JSON first 500 chars: ${it.take(500)}")
            val backups = json.decodeFromString(LegacyGameBackups.serializer(), it)
            Log.d(TAG, "tryLoadGameBackups: parsed Name='${backups.Name}', Backups.size=${backups.Backups.size}, DeviceHeads.size=${backups.DeviceHeads.size}")
            backups.copy(
                Name = backups.Name.ifBlank { gameName },
                DeviceHeads = backups.DeviceHeads.toMutableMap()
            )
        }
    }

    private suspend fun updateSyncState(
        cloudSettings: CloudSettings,
        currentDevice: CurrentDeviceContext,
        gameName: String,
        head: String
    ) = withContext(Dispatchers.IO) {
        val syncState = loadSyncState(cloudSettings, currentDevice)
        val syncedAt = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", Locale.US).apply {
            timeZone = TimeZone.getTimeZone("UTC")
        }.format(Date())

        val updatedGames = syncState.Games.toMutableMap()
        updatedGames[gameName] = LegacySyncStateItem(
            LastKnownLocalHead = head,
            LastKnownRemoteHead = head,
            LastSyncResult = "success",
            LastSyncAt = syncedAt,
            PendingAction = "none"
        )

        val updatedState = syncState.copy(
            CurrentDeviceId = currentDevice.deviceId,
            ConfigState = LegacySyncStateItem(
                LastKnownLocalHead = null,
                LastKnownRemoteHead = null,
                LastSyncResult = "success",
                LastSyncAt = syncedAt,
                PendingAction = "none"
            ),
            Games = updatedGames
        )

        val jsonStr = json.encodeToString(LegacySyncState.serializer(), updatedState)
        val saveDataRoot = getSaveDataRootKey(cloudSettings)
        uploadUtf8Json(cloudSettings.backend, "$saveDataRoot/sync_state.json", jsonStr)
    }

    private suspend fun loadSyncState(
        cloudSettings: CloudSettings,
        currentDevice: CurrentDeviceContext
    ): LegacySyncState = withContext(Dispatchers.IO) {
        val key = "${getSaveDataRootKey(cloudSettings)}/sync_state.json"
        val jsonStr = tryDownloadUtf8String(cloudSettings.backend, key)
        if (jsonStr != null) {
            json.decodeFromString(LegacySyncState.serializer(), jsonStr)
        } else {
            LegacySyncState(
                SchemaVersion = 1,
                BackendFingerprint = buildBackendFingerprint(cloudSettings),
                CurrentDeviceId = currentDevice.deviceId
            )
        }
    }

    // --- HTTP / S3 methods ---

    private suspend fun uploadFile(backend: CloudBackendSettings, objectKey: String, file: File) {
        file.inputStream().use { stream ->
            val bytes = stream.readBytes()
            val payloadHash = sha256Hex(bytes)
            sendRequest(backend, "PUT", objectKey, emptyMap(), bytes, payloadHash, "application/octet-stream")
        }
    }

    private suspend fun uploadUtf8Json(backend: CloudBackendSettings, objectKey: String, jsonStr: String) {
        val bytes = jsonStr.toByteArray(Charsets.UTF_8)
        val payloadHash = sha256Hex(bytes)
        sendRequest(backend, "PUT", objectKey, emptyMap(), bytes, payloadHash, "application/json; charset=utf-8")
    }

    private suspend fun tryDownloadUtf8String(backend: CloudBackendSettings, objectKey: String): String? {
        return try {
            val response = sendRequest(backend, "GET", objectKey, emptyMap(), null, EMPTY_PAYLOAD_HASH, null)
            String(response, Charsets.UTF_8)
        } catch (e: NotFoundException) {
            null
        }
    }

    private suspend fun downloadFile(backend: CloudBackendSettings, objectKey: String, destination: File) {
        val response = sendRequest(backend, "GET", objectKey, emptyMap(), null, EMPTY_PAYLOAD_HASH, null)
        destination.parentFile?.mkdirs()
        destination.writeBytes(response)
    }

    private class NotFoundException : Exception()

    private suspend fun sendRequest(
        backend: CloudBackendSettings,
        method: String,
        objectKey: String,
        query: Map<String, String>,
        body: ByteArray?,
        payloadHash: String,
        contentType: String?
    ): ByteArray = withContext(Dispatchers.IO) {
        val endpoint = backend.endpoint.trimEnd('/')
        val bucket = backend.bucket
        val region = backend.region.ifBlank { "us-east-1" }

        val canonicalUri = buildCanonicalUri(endpoint, bucket, objectKey)
        val canonicalQuery = buildCanonicalQueryString(query)

        val timestamp = Date()
        val amzDate = SimpleDateFormat("yyyyMMdd'T'HHmmss'Z'", Locale.US).apply {
            timeZone = TimeZone.getTimeZone("UTC")
        }.format(timestamp)
        val shortDate = SimpleDateFormat("yyyyMMdd", Locale.US).apply {
            timeZone = TimeZone.getTimeZone("UTC")
        }.format(timestamp)

        val url = "$endpoint$canonicalUri${if (canonicalQuery.isNotEmpty()) "?$canonicalQuery" else ""}"
        val urlObj = java.net.URL(url)
        val hostHeader = if (urlObj.port == -1 || urlObj.port == urlObj.defaultPort) urlObj.host else "${urlObj.host}:${urlObj.port}"

        val canonicalHeaders = "host:$hostHeader\n" +
            "x-amz-content-sha256:$payloadHash\n" +
            "x-amz-date:$amzDate\n"
        val signedHeaders = "host;x-amz-content-sha256;x-amz-date"

        val canonicalRequest = "$method\n$canonicalUri\n$canonicalQuery\n$canonicalHeaders\n$signedHeaders\n$payloadHash"
        val credentialScope = "$shortDate/$region/s3/aws4_request"
        val stringToSign = "AWS4-HMAC-SHA256\n$amzDate\n$credentialScope\n${sha256Hex(canonicalRequest.toByteArray(Charsets.UTF_8))}"

        val signature = createSignature(backend.secret_access_key, shortDate, region, stringToSign)
        val authorization = "AWS4-HMAC-SHA256 Credential=${backend.access_key_id}/$credentialScope, SignedHeaders=$signedHeaders, Signature=$signature"

        val requestBuilder = Request.Builder()
            .url(url)
            .header("Host", hostHeader)
            .header("x-amz-content-sha256", payloadHash)
            .header("x-amz-date", amzDate)
            .header("Authorization", authorization)

        when (method) {
            "GET" -> requestBuilder.get()
            "PUT" -> {
                val mediaType = (contentType ?: "application/octet-stream").toMediaType()
                requestBuilder.put((body ?: ByteArray(0)).toRequestBody(mediaType))
            }
            "DELETE" -> requestBuilder.delete()
        }

        Log.d(TAG, "sendRequest: $method $url")
        val response = client.newCall(requestBuilder.build()).execute()
        val responseBody = response.body?.bytes() ?: ByteArray(0)
        Log.d(TAG, "sendRequest: response code=${response.code}, bodyLen=${responseBody.size}")

        if (response.code == HttpURLConnection.HTTP_NOT_FOUND) {
            response.close()
            throw NotFoundException()
        }

        if (!response.isSuccessful) {
            val bodyStr = String(responseBody, Charsets.UTF_8)
            response.close()
            throw IllegalStateException("Cloud request failed for '$objectKey'. Status ${response.code}: $bodyStr")
        }

        response.close()
        responseBody
    }

    // --- AWS4 signing helpers ---

    private fun buildCanonicalUri(endpoint: String, bucket: String, objectKey: String): String {
        val uri = java.net.URI(endpoint)
        val segments = mutableListOf<String>()
        if (uri.path.isNotEmpty() && uri.path != "/") {
            segments.addAll(uri.path.trim('/').split('/').filter { it.isNotEmpty() })
        }
        segments.add(bucket)
        if (objectKey.isNotEmpty()) {
            segments.addAll(objectKey.split('/').filter { it.isNotEmpty() })
        }
        return "/" + segments.joinToString("/") { urlEncodePathSegment(it) }
    }

    private fun buildCanonicalQueryString(query: Map<String, String>): String {
        return query.entries
            .filter { it.key.isNotBlank() }
            .sortedBy { it.key }
            .joinToString("&") { "${urlEncode(it.key)}=${urlEncode(it.value)}" }
    }

    private fun urlEncode(value: String): String = URLEncoder.encode(value, "UTF-8")
        .replace("+", "%20")
        .replace("*", "%2A")
        .replace("%7E", "~")

    private fun urlEncodePathSegment(value: String): String = urlEncode(value).replace("%2F", "/")

    private fun createSignature(secretKey: String, shortDate: String, region: String, stringToSign: String): String {
        val kSecret = "AWS4$secretKey".toByteArray(Charsets.UTF_8)
        val kDate = hmacSha256(kSecret, shortDate)
        val kRegion = hmacSha256(kDate, region)
        val kService = hmacSha256(kRegion, "s3")
        val kSigning = hmacSha256(kService, "aws4_request")
        return bytesToHex(hmacSha256(kSigning, stringToSign))
    }

    private fun hmacSha256(key: ByteArray, data: String): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(key, "HmacSHA256"))
        return mac.doFinal(data.toByteArray(Charsets.UTF_8))
    }

    private fun sha256Hex(data: ByteArray): String = bytesToHex(MessageDigest.getInstance("SHA-256").digest(data))

    private fun bytesToHex(bytes: ByteArray): String = bytes.joinToString("") { "%02x".format(it) }

    // --- Key path helpers ---

    private fun getSaveDataRootKey(cloudSettings: CloudSettings): String {
        return "${cloudSettings.root_path.trimEnd('/')}/save_data"
    }

    private fun getGameRootKey(cloudSettings: CloudSettings, gameName: String): String {
        return "${getSaveDataRootKey(cloudSettings)}/$gameName"
    }

    private fun resolveCurrentHead(backups: LegacyGameBackups, deviceId: String): String? {
        return resolveCurrentBackup(backups, deviceId)?.Date
    }

    private fun resolveCurrentBackup(backups: LegacyGameBackups, deviceId: String): LegacyBackupEntry? {
        backups.DeviceHeads[deviceId]?.let { head ->
            if (head.isNotBlank()) {
                val headed = backups.Backups
                    .filter { it.DeviceId.equals(deviceId, ignoreCase = true) }
                    .sortedByDescending { if (it.Date == head) 1 else 0 }
                    .let { list ->
                        list.firstOrNull()
                    }
                if (headed != null) return headed
            }
        }

        val deviceBackup = backups.Backups
            .filter { it.DeviceId.equals(deviceId, ignoreCase = true) }
            .maxByOrNull { it.Date }
        if (deviceBackup != null) return deviceBackup

        return backups.Backups.maxByOrNull { it.Date }
    }

    private fun findBackup(backups: LegacyGameBackups, backupDate: String, deviceId: String?): LegacyBackupEntry? {
        if (!deviceId.isNullOrBlank()) {
            backups.Backups.firstOrNull {
                it.Date == backupDate && it.DeviceId.equals(deviceId, ignoreCase = true)
            }?.let { return it }
        }
        return backups.Backups.firstOrNull { it.Date == backupDate }
    }

    private fun resolveArchiveKey(entry: LegacyBackupEntry, cloudSettings: CloudSettings, gameName: String): String {
        if (entry.Path.isNotBlank()) {
            var relativePath = entry.Path.replace('/', '\\').trim()
            if (relativePath.startsWith(".\\")) relativePath = relativePath.drop(2)
            else if (relativePath.startsWith("./")) relativePath = relativePath.drop(2)

            val saveDataMarker = "\\save_data\\"
            val markerIndex = relativePath.indexOf(saveDataMarker, ignoreCase = true)
            if (markerIndex >= 0) {
                relativePath = "save_data\\" + relativePath.drop(markerIndex + saveDataMarker.length)
            }

            val configuredRoot = getSaveDataRootKey(cloudSettings).replace('/', '\\')
            if (relativePath.startsWith("save_data\\", ignoreCase = true)) {
                relativePath = configuredRoot + "\\" + relativePath.drop("save_data\\".length)
            }

            val parts = relativePath.split('\\').filter { it.isNotBlank() && it != "." }
            if (parts.isNotEmpty()) {
                return parts.joinToString("/")
            }
        }
        return "${getGameRootKey(cloudSettings, gameName)}/${entry.Date}.zip"
    }

    private fun parseBackupTimestamp(value: String?): String? {
        return try {
            val sdf = SimpleDateFormat("yyyy-MM-dd_HH-mm-ss", Locale.US)
            value?.let { sdf.parse(it) }?.let { SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.US).format(it) }
        } catch (_: Exception) {
            null
        }
    }

    private fun buildBackendFingerprint(cloudSettings: CloudSettings): String {
        val b = cloudSettings.backend
        return "/${cloudSettings.root_path.trim('/')}|{\"type\":\"${b.type}\",\"endpoint\":\"${b.endpoint}\",\"bucket\":\"${b.bucket}\",\"region\":\"${b.region}\",\"access_key_id\":\"${b.access_key_id}\",\"secret_access_key\":\"${b.secret_access_key}\"}"
    }
}
