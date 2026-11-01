package com.eflay.gamesavemanager.service

import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import java.io.File
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.nio.file.Files
import java.security.MessageDigest

class NfsMwSaveServiceTest {
    @Test
    fun personalEditWritesRequestedValuesAndRejectsInvalidChanges() = runBlocking {
        val root = Files.createTempDirectory("rgsm-nfs-kotlin-").toFile()
        try {
            val editor = NfsMwSaveService(root, File(requireNotNull(System.getProperty("nfsTemplatesDir"))))
            val file = createSave(root, 15, 0)
            val before = file.readBytes()
            val saved = editor.save(file.path, "DRIVER", 654321, 60000)
            assertEquals("DRIVER", saved.alias)
            assertEquals(654321L, saved.money)
            assertEquals(60000L, saved.bounty)
            assertEquals(saved, editor.read(file.path))
            assertArrayEquals(before, requireNotNull(file.parentFile).listFiles()!!.single { it.name.startsWith("PLAYER.bak_") }.readBytes())
            val current = file.readBytes()
            try { editor.save(file.path, "中文", 1, 60000); fail("non-ASCII name accepted") } catch (_: IllegalArgumentException) { }
            try { editor.save(file.path, "DRIVER", 1, 100); fail("bounty below garage total accepted") } catch (_: IllegalArgumentException) { }
            assertArrayEquals(current, file.readBytes())
        } finally { root.deleteRecursively() }
    }

    @Test
    fun bossReadyUsesEachStageAndKeepsProfileVehiclesAndBackup() = runBlocking {
        val root = Files.createTempDirectory("rgsm-nfs-kotlin-").toFile()
        try {
            val editor = NfsMwSaveService(root, File(requireNotNull(System.getProperty("nfsTemplatesDir"))))
            for (stage in 1..15) {
                val file = createSave(root, stage, 20_000_000)
                val original = file.readBytes()
                val result = editor.bossReady(file.path)
                val modified = file.readBytes()
                assertEquals(stage, result.stage)
                assertEquals("PLAYER", result.alias)
                assertEquals(123456L, result.money)
                assertEquals(20_005_000L, result.bounty)
                assertArrayEquals(original.copyOfRange(0xE2ED, 0xE2ED + 0x38), modified.copyOfRange(0xE2ED, 0xE2ED + 0x38))
                assertArrayEquals(original, requireNotNull(file.parentFile).listFiles()!!.single { it.name.startsWith("PLAYER.bak_") }.readBytes())
                assertArrayEquals(MessageDigest.getInstance("MD5").digest(modified.copyOfRange(0x44, 0x4034)), modified.copyOfRange(0x34, 0x44))
            }
        } finally { root.deleteRecursively() }
    }

    @Test
    fun bossReadyAddsMissingBountyAndRejectsInvalidStageWithoutWriting() = runBlocking {
        val root = Files.createTempDirectory("rgsm-nfs-kotlin-").toFile()
        try {
            val editor = NfsMwSaveService(root, File(requireNotNull(System.getProperty("nfsTemplatesDir"))))
            val valid = createSave(root, 15, 0)
            assertEquals(21_550L, editor.bossReady(valid.path).bounty)
            val invalid = createSave(root, 0, 0)
            val before = invalid.readBytes()
            try { editor.bossReady(invalid.path); fail("Expected invalid stage to be rejected") } catch (_: IllegalArgumentException) { }
            assertArrayEquals(before, invalid.readBytes())
            assertFalse(requireNotNull(invalid.parentFile).listFiles()!!.any { it.name.contains(".bak_") })
        } finally { root.deleteRecursively() }
    }

    private fun createSave(root: File, stage: Int, sold: Int): File {
        val folder = File(root, "save-$stage").apply { mkdirs() }
        val bytes = ByteArray(0xF86C)
        fun put(offset: Int, value: Int) { ByteBuffer.wrap(bytes, offset, 4).order(ByteOrder.LITTLE_ENDIAN).putInt(value) }
        put(4, bytes.size)
        "Game".toByteArray().copyInto(bytes, 0x44)
        bytes[0x4038] = stage.toByte()
        put(0x4039, 123456)
        "PLAYER".toByteArray().copyInto(bytes, 0x5A31)
        for (slot in 0 until 25) bytes[0xE2ED + slot * 0x38] = 0xff.toByte()
        bytes[0xE2ED] = 0
        bytes[0xE2ED + 1] = 0xcd.toByte(); bytes[0xE2ED + 10] = 0xcd.toByte(); bytes[0xE2ED + 11] = 0xcd.toByte()
        put(0xE2ED + 0x10, 5000)
        put(0xE865, sold)
        return File(folder, "PLAYER").apply { writeBytes(bytes) }
    }
}
