package org.librestream.app

import org.junit.Assert.assertEquals
import org.junit.Test

class AdBlockTest {
    @Test
    fun blocksAdsAndLeavesVideoAlone() {
        val cases = listOf(
            "https://googleads.g.doubleclick.net/pagead/id" to true,
            "https://www.youtube.com/pagead/adview?x=1" to true,
            "https://www.youtube.com/api/stats/ads?ver=2" to true,
            "https://www.youtube.com/iframe_api" to false,
            "https://www.youtube.com/embed/dQw4w9WgXcQ" to false,
            "https://rr1---sn-abc.googlevideo.com/videoplayback?id=1" to false,
            "https://i.ytimg.com/vi/dQw4w9WgXcQ/mqdefault.jpg" to false,
            "https://notdoubleclick.net.example.com/x" to false,
            "no es una url" to false,
        )
        for ((url, blocked) in cases) assertEquals(url, blocked, AdBlock.isAd(url))
    }
}
