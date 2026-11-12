package org.librestream.app

import android.annotation.SuppressLint
import android.app.Activity
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.view.View
import android.view.ViewGroup
import android.webkit.ValueCallback
import android.webkit.WebChromeClient
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.FrameLayout
import androidx.webkit.WebViewAssetLoader
import androidx.webkit.WebViewCompat
import androidx.webkit.WebViewFeature
import java.io.ByteArrayInputStream
import java.io.File

/** Envoltorio nativo de la interfaz compartida (app/dist/index.html) dentro de un WebView. */
class MainActivity : Activity() {

    private lateinit var web: WebView
    private lateinit var root: FrameLayout
    private var customView: View? = null
    private var customCallback: WebChromeClient.CustomViewCallback? = null
    private var fileCallback: ValueCallback<Array<Uri>>? = null
    private var pendingSave: String? = null

    /** Interruptor de anuncios (lo cambia la interfaz desde Ajustes). Se lee en el hilo de red del WebView. */
    @Volatile
    var adBlockEnabled: Boolean = true

    @SuppressLint("SetJavaScriptEnabled")
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        WebView.setWebContentsDebuggingEnabled(BuildConfig.DEBUG)
        // Android 13+: la notificación de reproducción necesita permiso (si se rechaza, la música sigue sonando)
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS) != android.content.pm.PackageManager.PERMISSION_GRANTED) {
            requestPermissions(arrayOf(android.Manifest.permission.POST_NOTIFICATIONS), 1)
        }

        root = FrameLayout(this).apply { setBackgroundColor(BACKGROUND) }
        web = WebView(this).apply { setBackgroundColor(BACKGROUND) }
        root.addView(web, ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)
        setContentView(root)
        root.fitsSystemWindows = true

        web.settings.apply {
            javaScriptEnabled = true
            domStorageEnabled = true                       // localStorage: biblioteca, listas y ajustes
            mediaPlaybackRequiresUserGesture = false       // la canción empieza al pulsar la tarjeta
            allowFileAccess = false
            allowContentAccess = false
            mixedContentMode = WebSettings.MIXED_CONTENT_NEVER_ALLOW
        }

        // Sirve la interfaz y las descargas bajo un origen https (el reproductor de YouTube lo exige)
        val downloads = File(filesDir, "downloads").apply { mkdirs() }
        val loader = WebViewAssetLoader.Builder()
            .addPathHandler("/assets/", WebViewAssetLoader.AssetsPathHandler(this))
            .addPathHandler("/media/", WebViewAssetLoader.InternalStoragePathHandler(this, downloads))
            .build()

        web.webViewClient = object : WebViewClient() {
            override fun shouldInterceptRequest(view: WebView, request: WebResourceRequest): WebResourceResponse? {
                if (adBlockEnabled && AdBlock.isAd(request.url.toString())) {
                    return WebResourceResponse("text/plain", "utf-8", 403, "Blocked", emptyMap(), ByteArrayInputStream(ByteArray(0)))
                }
                return loader.shouldInterceptRequest(request.url)
            }

            // Ayuda de pruebas (emulador): --es route home|music|videos|library|settings  --es action play
            private var hookDone = false
            override fun onPageFinished(view: WebView, url: String?) {
                if (hookDone) return
                hookDone = true
                val route = this@MainActivity.intent.getStringExtra("route")
                if (route in setOf("home", "music", "videos", "library", "settings")) view.evaluateJavascript("location.hash='#/$route'", null)
                if (this@MainActivity.intent.getStringExtra("action") == "play") {
                    view.postDelayed({ view.evaluateJavascript("var c=document.querySelector('.card'); if(c) c.click();", null) }, 8000)
                }
            }

            // Los enlaces externos se abren en el navegador; la propia app y los iframes siguen dentro
            override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
                if (request.url.host == ASSET_HOST || !request.isForMainFrame) return false
                try { startActivity(Intent(Intent.ACTION_VIEW, request.url)) } catch (_: Exception) { /* sin navegador */ }
                return true
            }
        }

        web.webChromeClient = object : WebChromeClient() {
            // Pantalla completa del vídeo de YouTube
            override fun onShowCustomView(view: View, callback: CustomViewCallback) {
                if (customView != null) { callback.onCustomViewHidden(); return }
                customView = view
                customCallback = callback
                root.addView(view, ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)
                web.visibility = View.GONE
            }

            // Selector de archivos del sistema (importar una lista desde un CSV, JSON o TXT)
            override fun onShowFileChooser(view: WebView, callback: ValueCallback<Array<Uri>>, params: FileChooserParams): Boolean {
                fileCallback?.onReceiveValue(null)
                fileCallback = callback
                return try {
                    @Suppress("DEPRECATION")
                    startActivityForResult(params.createIntent(), FILE_REQUEST)
                    true
                } catch (e: Exception) { fileCallback = null; false }
            }

            override fun onHideCustomView() {
                customView?.let { root.removeView(it) }
                customView = null
                customCallback?.onCustomViewHidden()
                customCallback = null
                web.visibility = View.VISIBLE
            }
        }

        web.addJavascriptInterface(NativeBridge(this, web), "AndroidBridge")

        // Ecualizador: se inyecta en el iframe de YouTube antes de que cargue su contenido
        if (WebViewFeature.isFeatureSupported(WebViewFeature.DOCUMENT_START_SCRIPT)) {
            val eq = assets.open("eq.js").bufferedReader().use { it.readText() }
            WebViewCompat.addDocumentStartJavaScript(web, eq, setOf("https://www.youtube.com", "https://www.youtube-nocookie.com"))
        }

        web.loadUrl("https://$ASSET_HOST/assets/index.html")
    }

    @Deprecated("Deprecated in Java")
    override fun onBackPressed() {
        when {
            customView != null -> web.webChromeClient?.onHideCustomView()
            else -> web.evaluateJavascript("(window.__lsBack && window.__lsBack()) === true") { handled ->
                // Primero se cierra lo que esté encima (Reproduciendo, cola, menús); si no había nada, se vuelve a la página anterior
                if (handled != "true") { if (web.canGoBack()) web.goBack() else finish() }
            }
        }
    }

    /** Guarda un texto (copia de la biblioteca) donde elija el usuario con el selector del sistema. */
    fun saveText(name: String, text: String) {
        runOnUiThread {
            pendingSave = text
            val i = Intent(Intent.ACTION_CREATE_DOCUMENT).apply {
                addCategory(Intent.CATEGORY_OPENABLE)
                type = "application/json"
                putExtra(Intent.EXTRA_TITLE, name)
            }
            try {
                @Suppress("DEPRECATION")
                startActivityForResult(i, SAVE_REQUEST)
            } catch (e: Exception) { pendingSave = null }
        }
    }

    @Deprecated("Deprecated in Java")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        if (requestCode == SAVE_REQUEST) {
            val uri = data?.data
            val text = pendingSave
            pendingSave = null
            if (resultCode == RESULT_OK && uri != null && text != null) {
                try { contentResolver.openOutputStream(uri)?.use { it.write(text.toByteArray(Charsets.UTF_8)) } } catch (_: Exception) { /* sin permiso o cancelado */ }
            }
            return
        }
        if (requestCode == FILE_REQUEST) {
            fileCallback?.onReceiveValue(WebChromeClient.FileChooserParams.parseResult(resultCode, data))
            fileCallback = null
            return
        }
        @Suppress("DEPRECATION")
        super.onActivityResult(requestCode, resultCode, data)
    }

    override fun onDestroy() {
        PlaybackService.listener = null
        PlaybackService.stop(this)
        web.destroy()
        super.onDestroy()
    }

    companion object {
        const val ASSET_HOST = "appassets.androidplatform.net"
        private const val BACKGROUND = 0xFF121214.toInt()
        private const val FILE_REQUEST = 4021
        private const val SAVE_REQUEST = 4022
    }
}
