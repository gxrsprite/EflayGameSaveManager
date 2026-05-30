package com.eflay.gamesavemanager.service

import android.content.Context
import android.net.Uri
import android.provider.DocumentsContract
import java.io.File

/**
 * Resolves content:// URIs from SAF pickers to real filesystem paths,
 * so the app can use direct java.io.File operations like the desktop C# version.
 * Requires MANAGE_EXTERNAL_STORAGE permission on API 30+.
 */
object StoragePathResolver {

    fun resolveToRealPath(context: Context, uri: Uri): String? {
        // Already a real path
        if (uri.scheme == "file") return uri.path

        if (uri.scheme != "content") return null

        // Try DocumentsContract for tree URIs
        if (DocumentsContract.isTreeUri(uri)) {
            return resolveTreeUri(uri)
        }

        // Try DocumentsContract for document URIs
        if (DocumentsContract.isDocumentUri(context, uri)) {
            return resolveDocumentUri(context, uri)
        }

        // Generic content URI — try _data column
        return resolveDataColumn(context, uri)
    }

    private fun resolveTreeUri(uri: Uri): String? {
        val docId = DocumentsContract.getTreeDocumentId(uri)
        // docId looks like "primary:Android/data/com.example" or "ABCD-1234:path"
        return docIdToPath(docId)
    }

    private fun resolveDocumentUri(context: Context, uri: Uri): String? {
        val docId = DocumentsContract.getDocumentId(uri)
        // docId looks like "primary:path/to/file" or "ABCD-1234:path/to/file"
        if (docId.contains(':')) {
            return docIdToPath(docId)
        }
        // Fallback to _data column query
        return resolveDataColumn(context, uri)
    }

    private fun docIdToPath(docId: String): String? {
        val colonIndex = docId.indexOf(':')
        if (colonIndex < 0) return null

        val volume = docId.substring(0, colonIndex)
        val relativePath = docId.substring(colonIndex + 1)

        val rootPath = when (volume.lowercase()) {
            "primary" -> "/storage/emulated/0"
            "home" -> "/storage/emulated/0"
            else -> "/storage/$volume"
        }

        // Validate the resolved root exists
        if (!File(rootPath).exists()) return null

        val fullPath = "$rootPath/$relativePath"
        return File(fullPath).canonicalPath
    }

    private fun resolveDataColumn(context: Context, uri: Uri): String? {
        return try {
            context.contentResolver.query(uri, arrayOf(android.provider.OpenableColumns.DISPLAY_NAME), null, null, null)?.use { cursor ->
                if (cursor.moveToFirst()) {
                    val name = cursor.getString(0)
                    // Last resort: try to find by name in common locations
                    // This is unreliable, so we return null and let caller handle it
                    null
                } else null
            }
        } catch (_: Exception) {
            null
        }
    }

    /**
     * Returns a human-readable description of the path for display.
     */
    fun describePath(path: String): String {
        return if (path.startsWith("content://")) {
            "Content URI (cannot use as direct path)"
        } else if (path.startsWith("/")) {
            path
        } else {
            "Invalid path: $path"
        }
    }
}
