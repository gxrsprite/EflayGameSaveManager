package com.eflay.gamesavemanager.service

import android.content.Context
import kotlinx.serialization.json.*
import java.io.File
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.nio.file.Files
import java.nio.file.StandardCopyOption
import java.security.MessageDigest
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.UUID

data class NfsProfileChoice(val path: String, val name: String)
data class NfsProfile(val alias: String, val money: Long, val bounty: Long, val stage: Int)

/** PC Most Wanted 2005 save format; uses the same offsets and stage templates as Core. */
class NfsMwSaveService private constructor(private val cacheRoot: () -> File, private val readAsset: (String) -> String) {
    constructor(context: Context) : this({ context.externalCacheDir ?: context.cacheDir }, { path -> context.assets.open(path).bufferedReader().use { it.readText() } })
    internal constructor(cache: File, templateDirectory: File) : this({ cache }, { path -> File(templateDirectory, path.substringAfterLast('/')).readText() })
    private val size = 0xF86C
    private val spans = listOf(0x34 to 0x4034, 0x4038 to 0x4039, 0x403D to 0x429D,
        0x42B9 to 0x5241, 0x5241 to 0x5739, 0x5B41 to 0x5B42, 0x5B62 to 0x5B64)
    private fun u32(data: ByteArray, offset: Int): Long = ByteBuffer.wrap(data, offset, 4).order(ByteOrder.LITTLE_ENDIAN).int.toLong() and 0xffffffffL
    private fun put32(data: ByteArray, offset: Int, value: Long) { ByteBuffer.wrap(data, offset, 4).order(ByteOrder.LITTLE_ENDIAN).putInt(value.toInt()) }
    private fun md5(data: ByteArray) = MessageDigest.getInstance("MD5").digest(data)
    private fun tempRoot(): File = File(cacheRoot(), "nfs9-edit/${UUID.randomUUID()}").apply { mkdirs() }

    private suspend fun stage(path: String, root: File): File {
        val direct = File(path)
        if (direct.exists()) return direct
        return ShizukuHelper.copyFromRestricted(path, root)
            ?: throw IllegalStateException("无法读取存档路径：$path。请检查路径及 Shizuku 授权。")
    }

    suspend fun list(paths: List<String>): List<NfsProfileChoice> {
        val result = mutableListOf<NfsProfileChoice>()
        for (path in paths.distinct()) {
            val root = tempRoot()
            try {
                val staged = stage(path, root)
                val candidates = if (staged.isDirectory) staged.walkTopDown().filter { it.isFile } else sequenceOf(staged)
                for (file in candidates) {
                    if (file.name.contains(".bak_") || file.length() != size.toLong()) continue
                    try {
                        readProfile(file.readBytes())
                        val original = if (staged.path == path) file.path else if (staged.isDirectory) File(path, file.relativeTo(staged).path).path else path
                        result.add(NfsProfileChoice(original, file.parentFile?.name ?: file.name))
                    } catch (_: Exception) { }
                }
            } finally { root.deleteRecursively() }
        }
        return result.distinctBy { it.path }.sortedBy { it.name.lowercase() }
    }

    suspend fun read(path: String): NfsProfile = process(path, false) { data -> readProfile(data) }
    suspend fun save(path: String, alias: String, money: Long, bounty: Long): NfsProfile = process(path, true) { data ->
        require(alias.length <= 16 && alias.all { it.code <= 127 }) { "档案名只能使用 ASCII 字符，最多 16 个字符。" }
        require(money in 0..0xffffffffL) { "金钱须在 0 到 4294967295 之间。" }
        setBounty(data, bounty)
        data.fill(0, 0x5A31, 0x5A31 + 0x24)
        alias.toByteArray(Charsets.US_ASCII).copyInto(data, 0x5A31)
        put32(data, 0x4039, money)
        readProfile(data)
    }
    suspend fun bossReady(path: String): NfsProfile = process(path, true) { data ->
        require(data.copyOfRange(0x44, 0x48).contentEquals("Game".toByteArray())) { "存档缺少 NFS MW 游戏区段标识。" }
        val stage = data[0x4038].toInt() and 255
        require(stage in 1..15) { "当前黑名单阶段无效，未修改存档。" }
        val template = Json.parseToJsonElement(readAsset("nfs_mw_boss_ready/stage${stage.toString().padStart(2, '0')}.json")).jsonObject
        require(template.getValue("stage").jsonPrimitive.int == stage) { "黑名单阶段模板不匹配。" }
        val items = template.getValue("spans").jsonArray
        require(items.size == spans.size) { "黑名单进度模板区段数量无效。" }
        val decoded = items.mapIndexed { index, element ->
            val item = element.jsonObject
            val (start, end) = spans[index]
            require(item.getValue("start").jsonPrimitive.int == start && item.getValue("end").jsonPrimitive.int == end) { "黑名单模板区段无效。" }
            val hex = item.getValue("hex").jsonPrimitive.content
            require(hex.length == (end - start) * 2) { "黑名单模板长度无效。" }
            ByteArray(hex.length / 2) { hex.substring(it * 2, it * 2 + 2).toInt(16).toByte() }
        }
        require(decoded[0].copyOfRange(16, 20).contentEquals("Game".toByteArray()) &&
            md5(decoded[0].copyOfRange(16, decoded[0].size)).contentEquals(decoded[0].copyOfRange(0, 16)) &&
            (decoded[1][0].toInt() and 255) == stage) { "黑名单模板校验失败。" }
        val oldBounty = readBounty(data)
        decoded.forEachIndexed { index, bytes -> bytes.copyInto(data, spans[index].first) }
        val required = template.getValue("minimum_total_bounty").jsonPrimitive.long
        if (oldBounty < required) setBounty(data, required)
        readProfile(data)
    }

    private suspend fun process(path: String, write: Boolean, action: (ByteArray) -> NfsProfile): NfsProfile {
        val root = tempRoot()
        try {
            val file = stage(path, root)
            require(file.isFile) { "请选择个人存档文件。" }
            val data = file.readBytes()
            readProfile(data)
            val fullHash = md5(data.copyOfRange(0, data.size - 16)).contentEquals(data.copyOfRange(data.size - 16, data.size))
            val result = action(data)
            if (write) {
                repair(data, fullHash)
                val stamp = SimpleDateFormat("yyyyMMdd_HHmmss_SSS", Locale.US).format(Date())
                val backup = File(file.path + ".bak_" + stamp)
                Files.copy(file.toPath(), backup.toPath())
                val temp = File(file.path + ".rgsm_tmp")
                try { temp.writeBytes(data); Files.move(temp.toPath(), file.toPath(), StandardCopyOption.REPLACE_EXISTING) }
                finally { temp.delete() }
                if (file.path != path) {
                    if (!ShizukuHelper.copyToRestricted(backup, path + ".bak_" + stamp)) throw IllegalStateException("无法在原目录创建备份，未写回存档。")
                    if (!ShizukuHelper.copyToRestricted(file, path)) throw IllegalStateException("写回存档失败，原存档备份已保留。")
                }
            }
            return result
        } finally { root.deleteRecursively() }
    }

    private fun readProfile(data: ByteArray): NfsProfile {
        require(data.size == size && u32(data, 4) == data.size.toLong()) { "不是已支持的 NFS MW 2005 PC 存档。" }
        val raw = data.copyOfRange(0x5A31, 0x5A31 + 0x24).takeWhile { it.toInt() != 0 }.toByteArray()
        require(raw.all { (it.toInt() and 255) < 128 }) { "档案名不是 ASCII，无法安全编辑。" }
        return NfsProfile(raw.toString(Charsets.US_ASCII), u32(data, 0x4039), readBounty(data), data[0x4038].toInt() and 255)
    }

    private fun readLiveBounty(data: ByteArray): Long {
        var sum = 0L
        for (slot in 0 until 25) {
            val off = 0xE2ED + slot * 0x38
            if ((data[off].toInt() and 255) == 255) continue
            require((data[off].toInt() and 255) == slot && (data[off + 1].toInt() and 255) == 0xcd && data[off + 7].toInt() == 0 && data[off + 8].toInt() == 0 && data[off + 9].toInt() == 0 && (data[off + 10].toInt() and 255) == 0xcd && (data[off + 11].toInt() and 255) == 0xcd) { "车库记录格式异常，已停止编辑。" }
            sum += u32(data, off + 0x10)
        }
        return sum
    }
    private fun readBounty(data: ByteArray): Long = readLiveBounty(data) + u32(data, 0xE865)
    private fun setBounty(data: ByteArray, target: Long) {
        val live = readLiveBounty(data)
        require(target >= live && target - live <= 0xffffffffL) { "总赏金须不低于车库赏金 $live，且已售车辆赏金不能超过 4294967295。" }
        put32(data, 0xE865, target - live)
    }

    private val crcTable = IntArray(256) { index ->
        var crc = index shl 24
        repeat(8) { crc = if ((crc and Int.MIN_VALUE) != 0) (crc shl 1) xor 0x04C11DB7 else crc shl 1 }
        crc
    }
    private fun crc(data: ByteArray, start: Int, end: Int): Long {
        var value = ByteBuffer.wrap(data, start, 4).order(ByteOrder.BIG_ENDIAN).int.inv()
        for (i in start + 4 until end) value = ((value shl 8) or (data[i].toInt() and 255)) xor crcTable[value ushr 24]
        return value.inv().toLong() and 0xffffffffL
    }
    private fun repair(data: ByteArray, fullHash: Boolean) {
        put32(data, 4, data.size.toLong())
        val digest = if (fullHash) md5(data.copyOfRange(0, data.size - 16)) else md5(data.copyOfRange(0x34, data.size - 16))
        digest.copyInto(data, data.size - 16)
        put32(data, 0x10, crc(data, 0x1C, 0x24))
        put32(data, 0x14, crc(data, 0x24, data.size))
        put32(data, 0x18, crc(data, 0, 0x18))
    }
}
