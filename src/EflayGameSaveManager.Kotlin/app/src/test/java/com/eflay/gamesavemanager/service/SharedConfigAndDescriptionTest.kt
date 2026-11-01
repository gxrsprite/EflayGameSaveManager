package com.eflay.gamesavemanager.service

import kotlinx.serialization.json.*
import org.junit.Assert.*
import org.junit.Test

class SharedConfigAndDescriptionTest {
    @Test
    fun configEditsPreserveUnknownFieldsAndIndependentUnits() {
        val old = Json.parseToJsonElement("""{"games":[{"name":"NFS9","external_launchers":{"pc":["tool"]},"save_paths":[{"id":0,"paths":{"pc":"C:/save","phone":"old"},"extra":"keep"},{"id":1,"paths":{"phone":"other"}}]}],"custom":{"keep":true}}""")
        val updated = Json.parseToJsonElement("""{"games":[{"name":"NFS9","save_paths":[{"id":0,"paths":{"pc":"C:/save","phone":"new"}},{"id":1,"paths":{"phone":"other"}}]}]}""")
        val merged = mergeConfig(old, updated).jsonObject
        val game = merged.getValue("games").jsonArray.single().jsonObject
        assertEquals("tool", game.getValue("external_launchers").jsonObject.getValue("pc").jsonArray.single().jsonPrimitive.content)
        assertEquals(2, game.getValue("save_paths").jsonArray.size)
        val first = game.getValue("save_paths").jsonArray[0].jsonObject
        assertEquals("keep", first.getValue("extra").jsonPrimitive.content)
        assertEquals("new", first.getValue("paths").jsonObject.getValue("phone").jsonPrimitive.content)
        assertTrue(merged.getValue("custom").jsonObject.getValue("keep").jsonPrimitive.boolean)
        val removedPath = Json.parseToJsonElement("""{"games":[{"name":"NFS9","save_paths":[{"id":0,"paths":{"pc":"C:/save"}}]}]}""")
        val removed = mergeConfig(old, removedPath).jsonObject.getValue("games").jsonArray.single().jsonObject.getValue("save_paths").jsonArray.single().jsonObject.getValue("paths").jsonObject
        assertFalse(removed.containsKey("phone"))
    }

    @Test
    fun descriptionTargetsOneDeviceAndPreservesOtherFields() {
        val raw = """{"backups":[{"date":"t","device_id":"pc","describe":"a","custom":12},{"date":"t","device_id":"phone","describe":"b"}],"device_heads":{"pc":"t"},"extra":5}"""
        val result = Json.parseToJsonElement(updateBackupDescriptionManifest(raw, "t", "PC", "  进度  ")).jsonObject
        val backups = result.getValue("backups").jsonArray
        assertEquals("进度", backups[0].jsonObject.getValue("describe").jsonPrimitive.content)
        assertEquals(12, backups[0].jsonObject.getValue("custom").jsonPrimitive.int)
        assertEquals("b", backups[1].jsonObject.getValue("describe").jsonPrimitive.content)
        assertEquals(5, result.getValue("extra").jsonPrimitive.int)
        try { updateBackupDescriptionManifest(raw, "t", "missing", "x"); fail("missing backup accepted") } catch (_: IllegalArgumentException) { }
        try { updateBackupDescriptionManifest(raw.replace("\"device_id\":\"phone\"", "\"device_id\":\"pc\""), "t", "pc", "x"); fail("ambiguous backup accepted") } catch (_: IllegalArgumentException) { }
    }
}
