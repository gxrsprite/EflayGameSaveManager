package com.eflay.gamesavemanager.service

import com.eflay.gamesavemanager.model.CurrentDeviceContext
import com.eflay.gamesavemanager.model.GameSnapshot
import java.io.ByteArrayOutputStream
import java.io.File
import java.security.SecureRandom
import java.util.zip.GZIPInputStream
import java.util.zip.GZIPOutputStream
import javax.crypto.Cipher
import javax.crypto.spec.IvParameterSpec
import javax.crypto.spec.SecretKeySpec

/**
 * Cult of the Lamb (咩咩启示录) save-format adapter, mirroring the Core C# plugin.
 * Canonical form: BOM-free UTF-8 JSON with the extensionless entry name.
 */
object CotLSavePlugin {

    enum class Target { Unknown, SteamPc, AndroidPort }

    private val UTF8_BOM = byteArrayOf(0xEF.toByte(), 0xBB.toByte(), 0xBF.toByte())

    fun matches(game: GameSnapshot): Boolean =
        isLambName(game.name) || isLambName(game.chineseName)

    fun resolveTarget(game: GameSnapshot, device: CurrentDeviceContext): Target {
        // Authoritative: the platform the client reported for this device.
        if (device.platform.contains("android", ignoreCase = true)) {
            return Target.AndroidPort
        }

        val paths = game.saveUnits.mapNotNull { it.path }
            .filter { it.isNotBlank() }
        for (path in paths) {
            // Normalize separators so a Windows-side config with backslashes still
            // resolves to the mobile port instead of the desktop format.
            val normalized = path.replace('\\', '/')
            if (normalized.contains("/Android/", ignoreCase = true) ||
                normalized.startsWith("/storage/", ignoreCase = true) ||
                normalized.startsWith("/sdcard/", ignoreCase = true) ||
                normalized.contains("/data/data/", ignoreCase = true)
            ) {
                return Target.AndroidPort
            }
        }
        // Device registered as the Android port: keep the mobile format even if the
        // snapshot path is temporarily redirected through an app cache folder.
        if (device.deviceName.contains("android", ignoreCase = true)) {
            return Target.AndroidPort
        }
        return Target.SteamPc
    }

    data class Canonical(val name: String, val json: ByteArray)

    /** Returns null when the file is not handled by this plugin (passthrough). */
    fun toCanonical(entryName: String, raw: ByteArray): Canonical? {
        if (raw.isEmpty()) return null
        // Android port: "ZB" + gzip(JSON)
        if (raw.size > 2 && raw[0] == 'Z'.code.toByte() && raw[1] == 'B'.code.toByte()) {
            return Canonical(stripJsonExt(entryName), stripBom(gunzip(raw.copyOfRange(2, raw.size))))
        }
        // Steam PC: 'E' + key(16) + iv(16) + AES-128-CBC(JSON)
        if (raw.size > 33 && raw[0] == 'E'.code.toByte()) {
            val plain = try {
                aesDecrypt(raw.copyOfRange(1, 17), raw.copyOfRange(17, 33), raw.copyOfRange(33, raw.size))
            } catch (_: Exception) {
                return null
            }
            if (!isJson(plain)) return null
            return Canonical(stripJsonExt(entryName), stripBom(plain))
        }
        // Already canonical
        if (isJson(raw)) {
            return Canonical(stripJsonExt(entryName), stripBom(raw))
        }
        return null
    }

    fun fromCanonical(canonicalJson: ByteArray, target: Target): ByteArray {
        return when (target) {
            Target.AndroidPort -> zbPrefix(gzip(addBom(canonicalJson)))
            else -> {
                val key = randomBytes(16)
                val iv = randomBytes(16)
                steamHeader(aesEncrypt(stripBom(canonicalJson), key, iv), key, iv)
            }
        }
    }

    fun toTargetName(canonicalName: String, target: Target): String {
        return when (target) {
            Target.AndroidPort -> stripJsonExt(canonicalName)
            else -> if (canonicalName.endsWith(".json", ignoreCase = true)) canonicalName
            else "$canonicalName.json"
        }
    }

    /** Backup hook: rewrite recognized files under stagingRoot into canonical form, in place. */
    fun normalizeStaging(stagingRoot: File) {
        stagingRoot.walkTopDown().filter { it.isFile }.sortedBy { it.absolutePath }
            .forEach { file ->
                val canonical = toCanonical(file.name, file.readBytes()) ?: return@forEach
                file.writeBytes(canonical.json)
                val target = File(file.parentFile, canonical.name)
                if (target.absolutePath != file.absolutePath) {
                    if (target.exists()) target.delete()
                    file.renameTo(target)
                }
            }
    }

    /** Restore hook: convert canonical files under extractRoot into target encoding, in place. */
    fun encodeStaging(extractRoot: File, game: GameSnapshot, device: CurrentDeviceContext) {
        val target = resolveTarget(game, device)
        if (target == Target.Unknown) return
        extractRoot.walkTopDown().filter { it.isFile }.sortedBy { it.absolutePath }
            .forEach { file ->
                val raw = file.readBytes()
                if (isTargetForm(file.name, raw, target)) return@forEach
                val canonical = toCanonical(file.name, raw) ?: return@forEach
                val encoded = fromCanonical(canonical.json, target)
                val targetFile = File(file.parentFile, toTargetName(canonical.name, target))
                file.writeBytes(encoded)
                if (targetFile.absolutePath != file.absolutePath) {
                    if (targetFile.exists()) targetFile.delete()
                    file.renameTo(targetFile)
                }
            }
    }

    private fun isTargetForm(fileName: String, raw: ByteArray, target: Target): Boolean {
        if (raw.isEmpty()) return true
        val canonical = toCanonical(fileName, raw) ?: return true
        if (!fileName.equals(toTargetName(canonical.name, target), ignoreCase = true)) return false
        return when (target) {
            Target.SteamPc -> raw.size > 33 && raw[0] == 'E'.code.toByte()
            else -> raw.size > 2 && raw[0] == 'Z'.code.toByte() && raw[1] == 'B'.code.toByte()
        }
    }

    private fun isLambName(name: String?): Boolean {
        if (name.isNullOrBlank()) return false
        val n = name.replace(" ", "").lowercase()
        return n.contains("cultofthelamb") || n.contains("咩咩启示录")
    }

    private fun stripJsonExt(name: String): String =
        if (name.endsWith(".json", ignoreCase = true)) name.dropLast(5) else name

    private fun isJson(raw: ByteArray): Boolean {
        val probe = stripBom(raw)
        return probe.isNotEmpty() && probe[0] == '{'.code.toByte()
    }

    private fun stripBom(raw: ByteArray): ByteArray {
        if (raw.size >= 3 && raw[0] == UTF8_BOM[0] && raw[1] == UTF8_BOM[1] && raw[2] == UTF8_BOM[2]) {
            return raw.copyOfRange(3, raw.size)
        }
        return raw
    }

    private fun addBom(raw: ByteArray): ByteArray = UTF8_BOM + raw

    private fun gunzip(raw: ByteArray): ByteArray {
        GZIPInputStream(raw.inputStream()).use { gz ->
            val out = ByteArrayOutputStream()
            gz.copyTo(out)
            return out.toByteArray()
        }
    }

    private fun gzip(raw: ByteArray): ByteArray {
        val out = ByteArrayOutputStream()
        GZIPOutputStream(out).use { gz -> gz.write(raw) }
        return out.toByteArray()
    }

    private fun aesDecrypt(key: ByteArray, iv: ByteArray, cipher: ByteArray): ByteArray {
        val c = Cipher.getInstance("AES/CBC/PKCS5Padding")
        c.init(Cipher.DECRYPT_MODE, SecretKeySpec(key, "AES"), IvParameterSpec(iv))
        return c.doFinal(cipher)
    }

    private fun aesEncrypt(plain: ByteArray, key: ByteArray, iv: ByteArray): ByteArray {
        val c = Cipher.getInstance("AES/CBC/PKCS5Padding")
        c.init(Cipher.ENCRYPT_MODE, SecretKeySpec(key, "AES"), IvParameterSpec(iv))
        return c.doFinal(plain)
    }

    private fun randomBytes(n: Int): ByteArray {
        val b = ByteArray(n)
        SecureRandom().nextBytes(b)
        return b
    }

    private fun steamHeader(cipher: ByteArray, key: ByteArray, iv: ByteArray): ByteArray {
        val out = ByteArray(1 + 16 + 16 + cipher.size)
        out[0] = 'E'.code.toByte()
        key.copyInto(out, 1)
        iv.copyInto(out, 17)
        cipher.copyInto(out, 33)
        return out
    }

    private fun zbPrefix(gz: ByteArray): ByteArray {
        val out = ByteArray(2 + gz.size)
        out[0] = 'Z'.code.toByte()
        out[1] = 'B'.code.toByte()
        gz.copyInto(out, 2)
        return out
    }
}
