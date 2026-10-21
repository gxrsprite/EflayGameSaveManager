package com.eflay.gamesavemanager.service

import java.io.File

/**
 * Wildcard filtering for save units, mirroring the Core C# SavePathFilter.
 *
 * Patterns match a file's name and its forward-slash relative path,
 * case-insensitively. Wildcards: `*` (any run) and `?` (one character).
 * A trailing `/` excludes a whole sub-directory.
 */
object SavePathFilter {

    fun parsePatterns(text: String?): List<String> {
        if (text.isNullOrBlank()) return emptyList()
        return text.split(',', ';', '\r', '\n')
            .map { it.trim() }
            .filter { it.isNotEmpty() }
            .distinctBy { it.lowercase() }
    }

    fun formatPatterns(patterns: List<String>?): String =
        patterns?.joinToString("; ") ?: ""

    fun isExcluded(fileName: String, relativePath: String?, patterns: List<String>?): Boolean {
        if (patterns.isNullOrEmpty()) return false

        val normalizedRelative = (if (relativePath.isNullOrBlank()) fileName else relativePath)
            .replace('\\', '/')
            .trimStart('/')

        for (raw in patterns) {
            var pattern = raw.trim()
            if (pattern.isEmpty() || pattern.startsWith("#")) continue
            pattern = pattern.replace('\\', '/')

            if (pattern.endsWith('/')) {
                val directory = pattern.trimEnd('/')
                if (normalizedRelative.startsWith("$directory/", ignoreCase = true) ||
                    isWildcardMatch(normalizedRelative, "$directory/*")
                ) {
                    return true
                }
                continue
            }

            if (isWildcardMatch(fileName, pattern) || isWildcardMatch(normalizedRelative, pattern)) {
                return true
            }
        }

        return false
    }

    /** Case-insensitive glob match supporting `*` and `?`. */
    fun isWildcardMatch(input: String, pattern: String): Boolean {
        if (pattern.isEmpty()) return input.isEmpty()

        var inputIndex = 0
        var patternIndex = 0
        var starIndex = -1
        var matchIndex = 0

        while (inputIndex < input.length) {
            if (patternIndex < pattern.length &&
                (pattern[patternIndex] == '?' ||
                    pattern[patternIndex].uppercaseChar() == input[inputIndex].uppercaseChar())
            ) {
                inputIndex++
                patternIndex++
                continue
            }

            if (patternIndex < pattern.length && pattern[patternIndex] == '*') {
                starIndex = patternIndex
                matchIndex = inputIndex
                patternIndex++
                continue
            }

            if (starIndex >= 0) {
                patternIndex = starIndex + 1
                matchIndex++
                inputIndex = matchIndex
                continue
            }

            return false
        }

        while (patternIndex < pattern.length && pattern[patternIndex] == '*') {
            patternIndex++
        }

        return patternIndex == pattern.length
    }

    /** Copies a directory tree, skipping files matching [patterns]. */
    fun copyDirectory(source: File, destination: File, patterns: List<String>?) {
        destination.mkdirs()

        source.listFiles()?.forEach { entry ->
            val name = entry.name
            if (entry.isDirectory) {
                if (isExcluded(name, "$name/", patterns)) return@forEach
                copyDirectory(entry, File(destination, name), patterns)
            } else {
                if (isExcluded(name, name, patterns)) return@forEach
                entry.copyTo(File(destination, name), overwrite = true)
            }
        }
    }

    /**
     * Deletes a directory's contents except entries matching [patterns],
     * so "delete before apply" never removes locally-owned files.
     */
    fun deleteExcluding(directory: File, patterns: List<String>?) {
        if (!directory.exists() || !directory.isDirectory) return

        if (patterns.isNullOrEmpty()) {
            directory.deleteRecursively()
            return
        }

        directory.listFiles()?.forEach { entry ->
            val name = entry.name
            if (entry.isDirectory) {
                if (isExcluded(name, "$name/", patterns)) return@forEach
                deleteExcluding(entry, patterns)
                if (entry.listFiles()?.isEmpty() != false) entry.delete()
            } else {
                if (!isExcluded(name, name, patterns)) entry.delete()
            }
        }
    }
}
