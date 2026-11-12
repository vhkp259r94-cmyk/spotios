package org.librestream.app

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.drawable.Icon
import android.media.MediaMetadata
import android.media.session.MediaSession
import android.media.session.PlaybackState
import android.os.Build
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.os.PowerManager
import java.net.HttpURLConnection
import java.net.URL
import java.util.concurrent.Executors

/**
 * Servicio en primer plano con notificación multimedia: mantiene viva la música con la pantalla apagada
 * (sin él Android puede pausar o cerrar la app al bloquear el móvil) y da los botones de anterior / pausa / siguiente
 * en la notificación y la pantalla de bloqueo. La interfaz envía los datos; los botones vuelven a ella como eventos.
 * El audio lo sigue emitiendo el reproductor oficial de YouTube dentro del WebView.
 */
class PlaybackService : Service() {

    companion object {
        private const val CHANNEL = "nsound.playback"
        private const val NOTIFICATION_ID = 7
        const val ACTION_UPDATE = "org.librestream.app.UPDATE"
        const val ACTION_STATE = "org.librestream.app.STATE"
        const val ACTION_CMD = "org.librestream.app.CMD"
        const val ACTION_STOP = "org.librestream.app.STOP"

        /** Quién recibe las órdenes de los botones: (orden, posición en segundos). Se ejecuta en el hilo principal. */
        @Volatile var listener: ((String, Double) -> Unit)? = null
        @Volatile private var instance: PlaybackService? = null

        fun update(ctx: Context, title: String, artist: String, album: String, artwork: String, duration: Double, position: Double, playing: Boolean) {
            val i = Intent(ctx, PlaybackService::class.java).setAction(ACTION_UPDATE)
                .putExtra("title", title).putExtra("artist", artist).putExtra("album", album).putExtra("artwork", artwork)
                .putExtra("duration", duration).putExtra("position", position).putExtra("playing", playing)
            try {
                if (instance != null || Build.VERSION.SDK_INT < Build.VERSION_CODES.O) ctx.startService(i) else ctx.startForegroundService(i)
            } catch (e: Exception) { /* el sistema no permite arrancarlo ahora: la música sigue, sin notificación */ }
        }

        fun state(ctx: Context, position: Double, duration: Double, playing: Boolean) {
            if (instance == null) return
            val i = Intent(ctx, PlaybackService::class.java).setAction(ACTION_STATE)
                .putExtra("position", position).putExtra("duration", duration).putExtra("playing", playing)
            try { ctx.startService(i) } catch (e: Exception) { /* sin permiso para arrancarlo */ }
        }

        fun stop(ctx: Context) {
            try { ctx.stopService(Intent(ctx, PlaybackService::class.java)) } catch (e: Exception) { /* ya parado */ }
        }
    }

    private var session: MediaSession? = null
    private var wake: PowerManager.WakeLock? = null
    private val main = Handler(Looper.getMainLooper())
    private val io = Executors.newSingleThreadExecutor()

    private var title = ""
    private var artist = ""
    private var album = ""
    private var artworkUrl = ""
    private var art: Bitmap? = null
    private var duration = 0.0
    private var position = 0.0
    private var playing = false
    private var foreground = false

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        instance = this
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val ch = NotificationChannel(CHANNEL, "Reproducción", NotificationManager.IMPORTANCE_LOW)
            ch.setShowBadge(false)
            (getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager).createNotificationChannel(ch)
        }
        session = MediaSession(this, "Nsound").apply {
            setCallback(object : MediaSession.Callback() {
                override fun onPlay() { listener?.invoke("play", 0.0) }
                override fun onPause() { listener?.invoke("pause", 0.0) }
                override fun onSkipToNext() { listener?.invoke("next", 0.0) }
                override fun onSkipToPrevious() { listener?.invoke("prev", 0.0) }
                override fun onSeekTo(pos: Long) { listener?.invoke("seek", pos / 1000.0) }
            })
            isActive = true
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_UPDATE -> {
                title = intent.getStringExtra("title") ?: ""
                artist = intent.getStringExtra("artist") ?: ""
                album = intent.getStringExtra("album") ?: ""
                duration = intent.getDoubleExtra("duration", 0.0)
                position = intent.getDoubleExtra("position", 0.0)
                playing = intent.getBooleanExtra("playing", true)
                val url = intent.getStringExtra("artwork") ?: ""
                if (url != artworkUrl) { artworkUrl = url; art = null; loadArtwork(url) }
                refresh(true)
            }
            ACTION_STATE -> {
                position = intent.getDoubleExtra("position", position)
                val d = intent.getDoubleExtra("duration", duration)
                if (d > 0) duration = d
                val p = intent.getBooleanExtra("playing", playing)
                val changed = p != playing
                playing = p
                refresh(changed)
            }
            ACTION_CMD -> listener?.invoke(intent.getStringExtra("cmd") ?: "", 0.0)
            ACTION_STOP -> { stopSelf(); return START_NOT_STICKY }
        }
        // Si se arrancó con startForegroundService hay que llamar a startForeground enseguida
        if (!foreground) refresh(true)
        return START_NOT_STICKY
    }

    private fun refresh(notify: Boolean) {
        val s = session ?: return
        s.setMetadata(MediaMetadata.Builder()
            .putString(MediaMetadata.METADATA_KEY_TITLE, title)
            .putString(MediaMetadata.METADATA_KEY_ARTIST, artist)
            .putString(MediaMetadata.METADATA_KEY_ALBUM, album)
            .putLong(MediaMetadata.METADATA_KEY_DURATION, (duration * 1000).toLong())
            .apply { art?.let { putBitmap(MediaMetadata.METADATA_KEY_ALBUM_ART, it); putBitmap(MediaMetadata.METADATA_KEY_ART, it) } }
            .build())
        s.setPlaybackState(PlaybackState.Builder()
            .setActions(PlaybackState.ACTION_PLAY or PlaybackState.ACTION_PAUSE or PlaybackState.ACTION_PLAY_PAUSE or
                PlaybackState.ACTION_SKIP_TO_NEXT or PlaybackState.ACTION_SKIP_TO_PREVIOUS or PlaybackState.ACTION_SEEK_TO)
            .setState(if (playing) PlaybackState.STATE_PLAYING else PlaybackState.STATE_PAUSED, (position * 1000).toLong(), if (playing) 1f else 0f)
            .build())
        updateWakeLock()
        if (notify || !foreground) showNotification()
    }

    private fun updateWakeLock() {
        try {
            if (playing) {
                if (wake == null) wake = (getSystemService(Context.POWER_SERVICE) as PowerManager).newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "nsound:playback")
                if (wake?.isHeld == false) wake?.acquire(6 * 60 * 60 * 1000L)      // como mucho 6 horas seguidas
            } else if (wake?.isHeld == true) wake?.release()
        } catch (e: Exception) { /* sin permiso: se sigue sin bloqueo de CPU */ }
    }

    private fun action(icon: Int, label: String, cmd: String, code: Int): Notification.Action {
        val pi = PendingIntent.getService(this, code, Intent(this, PlaybackService::class.java).setAction(ACTION_CMD).putExtra("cmd", cmd),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        return Notification.Action.Builder(Icon.createWithResource(this, icon), label, pi).build()
    }

    private fun showNotification() {
        val open = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val stop = PendingIntent.getService(this, 9, Intent(this, PlaybackService::class.java).setAction(ACTION_STOP),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val b = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) Notification.Builder(this, CHANNEL) else Notification.Builder(this)
        b.setSmallIcon(android.R.drawable.ic_media_play)
            .setContentTitle(title.ifEmpty { "Nsound" })
            .setContentText(artist)
            .setSubText(album.ifEmpty { null })
            .setContentIntent(open)
            .setDeleteIntent(stop)
            .setOnlyAlertOnce(true)
            .setShowWhen(false)
            .setVisibility(Notification.VISIBILITY_PUBLIC)
            .setOngoing(playing)
            .addAction(action(android.R.drawable.ic_media_previous, "Anterior", "prev", 1))
            .addAction(if (playing) action(android.R.drawable.ic_media_pause, "Pausa", "pause", 2) else action(android.R.drawable.ic_media_play, "Reproducir", "play", 3))
            .addAction(action(android.R.drawable.ic_media_next, "Siguiente", "next", 4))
            .setStyle(Notification.MediaStyle().setMediaSession(session?.sessionToken).setShowActionsInCompactView(0, 1, 2))
        art?.let { b.setLargeIcon(it) }
        val n = b.build()
        try {
            if (playing || !foreground) {
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) startForeground(NOTIFICATION_ID, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PLAYBACK)
                else startForeground(NOTIFICATION_ID, n)
                foreground = true
            } else {
                // En pausa la notificación se puede descartar deslizando
                stopForeground(Service.STOP_FOREGROUND_DETACH)
                foreground = false
                (getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager).notify(NOTIFICATION_ID, n)
            }
        } catch (e: Exception) { /* sin permiso de notificaciones: la música sigue */ }
    }

    /** Solo se descargan carátulas de los servidores de imágenes de YouTube. */
    private fun loadArtwork(url: String) {
        val u = try { URL(url) } catch (e: Exception) { return }
        val host = u.host ?: return
        if (u.protocol != "https" || !(host.endsWith("googleusercontent.com") || host.endsWith("ggpht.com") || host.endsWith("ytimg.com"))) return
        io.execute {
            try {
                val c = (u.openConnection() as HttpURLConnection).apply { connectTimeout = 10_000; readTimeout = 10_000 }
                val bmp = c.inputStream.use { BitmapFactory.decodeStream(it) }
                c.disconnect()
                if (bmp != null) main.post { if (url == artworkUrl) { art = bmp; refresh(true) } }
            } catch (e: Exception) { /* sin carátula */ }
        }
    }

    override fun onTaskRemoved(rootIntent: Intent?) {
        // Se cierra la app desde recientes: se para también la música y la notificación
        stopSelf()
        super.onTaskRemoved(rootIntent)
    }

    override fun onDestroy() {
        try { if (wake?.isHeld == true) wake?.release() } catch (e: Exception) { /* ya liberado */ }
        session?.isActive = false
        session?.release()
        session = null
        instance = null
        super.onDestroy()
    }
}
