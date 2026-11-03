package com.accountabilibuddy.companion.ui

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.QrCodeScanner
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.accountabilibuddy.companion.sync.QrFrameDecoder
import com.accountabilibuddy.companion.ui.kit.AbInfoBanner
import com.accountabilibuddy.companion.ui.kit.AbPageSheet
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean

private const val SAME_CODE_PAUSE_MILLIS = 2_000L

/**
 * "Scan a link code" (ADR 57): the camera, opened only because the user tapped Scan, and the camera permission asked now, not before.
 * [onText] gets the text of each QR code seen and says whether it was a link code; on true the sheet closes, on false it says so and
 * scanning goes on. Nothing is saved or sent; the frames are decoded in memory. [onPaste] is the way out when the camera can't be used.
 */
@Composable
internal fun JoinScanSheet(onText: (String) -> Boolean, onPaste: () -> Unit, onClose: () -> Unit) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    var access by remember { mutableStateOf(cameraAccess(context)) }
    var askedThisOpening by rememberSaveable { mutableStateOf(false) }
    var waitingForAnswer by remember { mutableStateOf(false) }
    var notACode by remember { mutableStateOf(false) }

    val askForCamera = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        waitingForAnswer = false
        access = if (granted) CameraAccess.Granted else CameraAccess.Denied
    }

    // Asked once per opening, after the launcher above is registered (launching during composition would crash).
    LaunchedEffect(Unit) {
        if (access == CameraAccess.NotAsked && !askedThisOpening) {
            askedThisOpening = true
            waitingForAnswer = true
            askForCamera.launch(Manifest.permission.CAMERA)
        }
    }

    // Already asked and not granted, with no question on screen (after a rotation, or the permission taken away in settings): denied.
    val shown = if (access == CameraAccess.NotAsked && askedThisOpening && !waitingForAnswer) CameraAccess.Denied else access

    // Back from the app's settings page: whatever the user chose there counts.
    DisposableEffect(lifecycleOwner) {
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_RESUME && access != CameraAccess.Granted && cameraAccess(context) == CameraAccess.Granted) {
                access = CameraAccess.Granted
            }
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose { lifecycleOwner.lifecycle.removeObserver(observer) }
    }

    AbPageSheet(
        title = "Scan a link code",
        icon = Icons.Outlined.QrCodeScanner,
        onDismiss = onClose,
        footer = {
            AbTonalButton("Paste instead", onClick = onPaste, modifier = Modifier.fillMaxWidth())
            AbTextButton("Close", onClick = onClose, modifier = Modifier.align(Alignment.CenterHorizontally))
        },
    ) {
        when (shown) {
            CameraAccess.Granted -> {
                Text(
                    "On the other device, open Devices & sync and show its link code: \"Link a new device\" on a PC, \"Link a device\" on a phone. " +
                        "Hold this phone so the QR code fills the square.",
                    style = Ab.type.bodyMedium, color = Ab.colors.ink,
                )
                CameraPreview(
                    onText = { text ->
                        val accepted = onText(text)
                        notACode = !accepted
                        if (accepted) onClose()
                        accepted
                    },
                    onUnavailable = { access = CameraAccess.NoCamera },
                )
                if (notACode) AbInfoBanner("That isn't an AccountabiliBuddy link code. Still looking.", icon = Icons.Outlined.QrCodeScanner)
            }

            CameraAccess.NotAsked -> Text("Waiting for your answer to Android's camera question…", style = Ab.type.bodyMedium, color = Ab.colors.muted)

            CameraAccess.Denied -> {
                Text(
                    "The camera is off for AccountabiliBuddy, so it can't scan. It is used only here, to read this one code; nothing is saved or sent.",
                    style = Ab.type.bodyMedium, color = Ab.colors.ink,
                )
                AbTonalButton("Open the app's settings", onClick = { openAppSettings(context) }, modifier = Modifier.fillMaxWidth())
                Text("Or use Paste, or type the code.", style = Ab.type.small, color = Ab.colors.muted)
            }

            CameraAccess.NoCamera ->
                Text("This phone has no camera that can be used. Use Paste, or type the code.", style = Ab.type.bodyMedium, color = Ab.colors.ink)
        }
    }
}

private enum class CameraAccess { NotAsked, Granted, Denied, NoCamera }

private fun cameraAccess(context: Context): CameraAccess = when {
    !context.packageManager.hasSystemFeature(PackageManager.FEATURE_CAMERA_ANY) -> CameraAccess.NoCamera
    ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED -> CameraAccess.Granted
    else -> CameraAccess.NotAsked
}

private fun openAppSettings(context: Context) {
    context.startActivity(
        Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.fromParts("package", context.packageName, null)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
    )
}

/**
 * The live preview with frame analysis, bound to the screen's lifecycle and unbound when it leaves. Analysis runs on its own thread and
 * keeps only the newest frame. [onText] runs on the main thread and says whether the text was accepted; after an accepted text no
 * more frames are read. The same wrong code is not reported again for a moment, so the message doesn't flicker.
 */
@Composable
private fun CameraPreview(onText: (String) -> Boolean, onUnavailable: () -> Unit) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val previewView = remember { PreviewView(context).apply {
            // COMPATIBLE draws in a TextureView, so the rounded clip below applies to the picture.
            implementationMode = PreviewView.ImplementationMode.COMPATIBLE
            scaleType = PreviewView.ScaleType.FILL_CENTER
        } }

    DisposableEffect(lifecycleOwner) {
        val analysisThread: ExecutorService = Executors.newSingleThreadExecutor()
        val mainThread = ContextCompat.getMainExecutor(context)
        val finished = AtomicBoolean(false)
        val disposed = AtomicBoolean(false)
        val decoder = QrFrameDecoder()
        var lastRefused: Int? = null
        var lastRefusedAt = 0L
        var provider: ProcessCameraProvider? = null

        val preview = Preview.Builder().build().also { it.surfaceProvider = previewView.surfaceProvider }
        val analysis = ImageAnalysis.Builder()
            .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
            .build()
        analysis.setAnalyzer(analysisThread) { frame ->
            val text = if (finished.get()) closeUnread(frame) else readFrame(decoder, frame)
            if (text != null) {
                mainThread.execute {
                    val now = System.currentTimeMillis()
                    val repeat = text.hashCode() == lastRefused && now - lastRefusedAt < SAME_CODE_PAUSE_MILLIS
                    if (!finished.get() && !repeat) {
                        if (onText(text)) {
                            finished.set(true)
                        } else {
                            lastRefused = text.hashCode()
                            lastRefusedAt = now
                        }
                    }
                }
            }
        }

        val providerFuture = ProcessCameraProvider.getInstance(context)
        providerFuture.addListener({
            if (disposed.get()) return@addListener
            try {
                val cameraProvider = providerFuture.get()
                provider = cameraProvider
                cameraProvider.unbindAll()
                cameraProvider.bindToLifecycle(lifecycleOwner, CameraSelector.DEFAULT_BACK_CAMERA, preview, analysis)
            } catch (_: Exception) {
                // No back camera, or the camera is in use elsewhere: the screen falls back to Paste.
                onUnavailable()
            }
        }, mainThread)

        onDispose {
            disposed.set(true)
            finished.set(true)
            analysis.clearAnalyzer()
            provider?.unbind(preview, analysis)
            analysisThread.shutdown()
        }
    }

    AndroidView(factory = { previewView }, modifier = Modifier.fillMaxWidth().aspectRatio(1f).clip(RoundedCornerShape(16.dp)))
}

/** The QR text in one frame, or null. The frame is always closed, or the camera stops handing out frames. */
private fun readFrame(decoder: QrFrameDecoder, frame: ImageProxy): String? = frame.use { image ->
    val luma = image.planes[0]
    val buffer = luma.buffer
    val bytes = ByteArray(buffer.remaining())
    buffer.get(bytes)
    decoder.decode(bytes, luma.rowStride, image.width, image.height)
}

private fun closeUnread(frame: ImageProxy): String? {
    frame.close()
    return null
}
