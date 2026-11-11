package org.librestream.app

import java.net.URI

/** Mismas reglas que app/src/js/00-core.js (LS.isAdUrl): corta servidores de anuncios y rastreo conocidos. */
object AdBlock {
    private val hosts = listOf(
        "doubleclick.net", "googlesyndication.com", "googleadservices.com", "googletagservices.com",
        "googletagmanager.com", "google-analytics.com", "adservice.google.com", "2mdn.net",
        "imasdk.googleapis.com", "moatads.com", "scorecardresearch.com", "adsrvr.org", "amazon-adsystem.com",
    )
    private val youtubePaths = listOf("/pagead/", "/api/stats/ads", "/get_midroll_info", "/ptracking", "/youtubei/v1/player/ad_break")

    fun isAd(url: String): Boolean {
        val uri = try { URI(url) } catch (e: Exception) { return false }
        val host = uri.host?.lowercase() ?: return false
        if (hosts.any { host == it || host.endsWith(".$it") }) return true
        val youtube = host == "youtube.com" || host.endsWith(".youtube.com") || host.endsWith(".youtube-nocookie.com")
        val path = (uri.path ?: "").lowercase()
        return youtube && youtubePaths.any { path.startsWith(it) }
    }
}
