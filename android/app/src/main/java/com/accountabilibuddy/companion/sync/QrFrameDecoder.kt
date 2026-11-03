package com.accountabilibuddy.companion.sync

import com.google.zxing.BinaryBitmap
import com.google.zxing.ChecksumException
import com.google.zxing.FormatException
import com.google.zxing.NotFoundException
import com.google.zxing.PlanarYUVLuminanceSource
import com.google.zxing.common.HybridBinarizer
import com.google.zxing.qrcode.QRCodeReader

/**
 * Reads a QR code out of one camera frame (ADR 57). Only the frame's brightness (luma) plane is looked at, in memory; QR codes
 * only, in any orientation. Nothing is kept: the frame is dropped as soon as [decode] returns. One instance per analysis thread.
 */
class QrFrameDecoder {
    private val reader = QRCodeReader()

    /**
     * The text of the QR code in the frame, or null when there is none (the usual case: most frames hold no code).
     * [luma] is the frame's first (Y) plane with [rowStride] bytes per row, of which the first [width] are the picture.
     */
    fun decode(luma: ByteArray, rowStride: Int, width: Int, height: Int): String? {
        val source = PlanarYUVLuminanceSource(luma, rowStride, height, 0, 0, width, height, false)
        return try {
            reader.decode(BinaryBitmap(HybridBinarizer(source))).text
        } catch (_: NotFoundException) {
            null
        } catch (_: ChecksumException) {
            null
        } catch (_: FormatException) {
            null
        } catch (_: RuntimeException) {
            // Anything unexpected in a frame is only a frame without a code; it must not take the analysis thread down.
            null
        } finally {
            reader.reset()
        }
    }
}
