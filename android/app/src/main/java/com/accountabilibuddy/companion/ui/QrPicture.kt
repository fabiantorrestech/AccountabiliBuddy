package com.accountabilibuddy.companion.ui

import android.graphics.Bitmap
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import com.google.zxing.BarcodeFormat
import com.google.zxing.EncodeHintType
import com.google.zxing.qrcode.QRCodeWriter
import com.google.zxing.qrcode.decoder.ErrorCorrectionLevel

/** QR codes drawn on the phone (ADR 22): its own sync file for "Show as picture", and the group's link code. */
object QrPicture {
    /** [text] as a black-on-white QR code of [size] × [size] pixels, with a small quiet zone. */
    fun render(text: String, size: Int = 720): ImageBitmap {
        val hints = mapOf(EncodeHintType.MARGIN to 2, EncodeHintType.ERROR_CORRECTION to ErrorCorrectionLevel.M)
        val matrix = QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, size, size, hints)
        val pixels = IntArray(size * size) { index -> if (matrix[index % size, index / size]) BLACK else WHITE }
        return Bitmap.createBitmap(pixels, size, size, Bitmap.Config.ARGB_8888).asImageBitmap()
    }

    private const val BLACK = 0xFF000000.toInt()
    private const val WHITE = 0xFFFFFFFF.toInt()
}
