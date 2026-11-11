package org.librestream.app

import android.webkit.JavascriptInterface
import android.webkit.WebView
import org.json.JSONObject
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.util.concurrent.Executors

/**
 * Órdenes de la interfaz (JavaScript → Kotlin): leer YouTube/LRCLIB sin CORS, descargar contenido libre
 * y ajustar el bloqueo de anuncios. Cada llamada responde con window.__nativeResolve(id, ok, valor).
 */
class NativeBridge(private val activity: MainActivity, private val web: WebView) {

    private val io = Executors.newCachedThreadPool()
    private val fetchHosts = setOf("www.youtube.com", "lrclib.net", "vhkp259r94-cmyk.github.io")
    private val userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36"
    private val downloadsDir get() = File(activity.filesDir, "downloads").apply { mkdirs() }

    @JavascriptInterface
    fun call(id: Int, name: String, payload: String) {
        io.execute {
            try {
                val p = JSONObject(payload)
                when (name) {
                    "fetchText" -> resolve(id, true, JSONObject.quote(fetchText(p.getString("url"))))
                    "download" -> resolve(id, true, download(p).toString())
                    "deleteDownload" -> {
                        if (!p.isNull("file")) File(downloadsDir, safeName(p.getString("file"))).delete()
                        resolve(id, true, "\"ok\"")
                    }
                    "setAdBlock" -> {
                        activity.adBlockEnabled = p.optBoolean("enabled", true)
                        resolve(id, true, "\"ok\"")
                    }
                    else -> resolve(id, false, JSONObject.quote("Orden desconocida: $name"))
                }
            } catch (e: Exception) {
                resolve(id, false, JSONObject.quote(e.message ?: e.javaClass.simpleName))
            }
        }
    }

    /** `literal` ya es JavaScript válido (cadena entre comillas u objeto JSON). */
    private fun resolve(id: Int, ok: Boolean, literal: String) {
        web.post { web.evaluateJavascript("window.__nativeResolve($id,$ok,$literal)", null) }
    }

    private fun emit(literal: String) {
        web.post { web.evaluateJavascript("window.__nativeEvent && window.__nativeEvent($literal)", null) }
    }

    private fun safeName(name: String): String {
        val cleaned = File(name).name.replace(Regex("[^A-Za-z0-9._-]"), "_")
        return cleaned.ifEmpty { "descarga" }
    }

    private fun open(urlString: String): HttpURLConnection {
        val url = URL(urlString)
        require(url.protocol == "https") { "Solo se admiten direcciones https" }
        return (url.openConnection() as HttpURLConnection).apply {
            connectTimeout = 15_000
            readTimeout = 30_000
            instanceFollowRedirects = true
            setRequestProperty("User-Agent", userAgent)
            setRequestProperty("Accept-Language", "es-ES,es;q=0.9,en;q=0.8")
        }
    }

    private fun fetchText(urlString: String): String {
        val host = URL(urlString).host
        require(host in fetchHosts) { "Dirección no permitida" }
        val conn = open(urlString)
        try {
            val status = conn.responseCode
            if (status !in 200..299) throw IllegalStateException("HTTP $status")
            return conn.inputStream.use { stream ->
                val bytes = stream.readBytes()
                require(bytes.size <= 12_000_000) { "Respuesta demasiado grande" }
                String(bytes, Charsets.UTF_8)
            }
        } finally {
            conn.disconnect()
        }
    }

    private fun download(p: JSONObject): JSONObject {
        val id = p.getString("id")
        val file = File(downloadsDir, safeName(p.getString("filename")))
        val part = File(file.path + ".part")
        val conn = open(p.getString("url"))
        try {
            val status = conn.responseCode
            if (status !in 200..299) throw IllegalStateException("HTTP $status")
            val total = conn.contentLengthLong
            var written = 0L
            var lastEmit = 0L
            conn.inputStream.use { input ->
                part.outputStream().use { output ->
                    val buffer = ByteArray(64 * 1024)
                    while (true) {
                        val n = input.read(buffer)
                        if (n < 0) break
                        output.write(buffer, 0, n)
                        written += n
                        val now = System.currentTimeMillis()
                        if (total > 0 && now - lastEmit > 250) {
                            lastEmit = now
                            emit("{type:'progress',id:${JSONObject.quote(id)},p:${written.toDouble() / total}}")
                        }
                    }
                }
            }
            if (!part.renameTo(file)) { file.delete(); part.renameTo(file) }
            return JSONObject()
                .put("url", "https://${MainActivity.ASSET_HOST}/media/${file.name}")
                .put("bytes", file.length())
                .put("file", file.name)
        } catch (e: Exception) {
            part.delete()
            throw e
        } finally {
            conn.disconnect()
        }
    }
}
